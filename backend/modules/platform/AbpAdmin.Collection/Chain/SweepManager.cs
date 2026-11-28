using AbpAdmin.Collection.Ledger;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 每轮先核对在途交易，再分别处理冷却重试、排队、打油和待广播任务。各阶段优先处理等待最久的任务。
/// 归集代币与打油前均先落库签名尝试（<see cref="ColChainTxAttempt"/>）、哈希和 nonce，
/// 避免节点收到交易但响应丢失后重发或产生孤儿交易；超时重签与队头清理按 <see cref="BscGasPricePolicy"/> 严格抬价。
/// 任务状态按原尝试条件更新。确认与转账核对通过后，任务完结、余额和凭证在同一事务提交。
/// </summary>
public class SweepManager : DomainService
{
    private static readonly TimeSpan StageBudget = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ReceiptQueryBudget = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan SponsorHeadReplaceAfter = TimeSpan.FromMinutes(3);
    internal static readonly TimeSpan FuelSettleGrace = TimeSpan.FromMinutes(2);
    internal const decimal MinimumFuelBnb = 0.0005m;
    internal const int PreflightDeficitThreshold = 3;
    internal const int DiscoverMaxCallsPerRound = 16;
    internal const long DiscoverBlockChunkSpan = 5000;

    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly CollectionBalanceManager _balanceManager;
    private readonly CollectionLedgerPoster _ledgerPoster;
    private readonly ChainAlertService _alerts;
    private readonly IStringEncryptionService _encryptor;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IChainPushNotifier? _pushNotifier;

    public SweepManager(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        CollectionBalanceManager balanceManager,
        CollectionLedgerPoster ledgerPoster,
        ChainAlertService alerts,
        IStringEncryptionService encryptor,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        IChainPushNotifier? pushNotifier = null)
    {
        _dbContextProvider = dbContextProvider;
        _balanceManager = balanceManager;
        _ledgerPoster = ledgerPoster;
        _alerts = alerts;
        _encryptor = encryptor;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
        _pushNotifier = pushNotifier;
    }

    private string? MasterKey => _masterKey;
    private string? _masterKey;

    /// <summary>用于解密归集密钥的主密钥，由 worker 组装。为空时无法执行签名。</summary>
    public SweepManager WithMasterKey(string? masterKey)
    {
        _masterKey = masterKey;
        return this;
    }

    /// <summary>
    /// Gas Sponsor 队头看门狗（由 worker 每网每轮调用一次）：
    /// 先经 VerifyDeployedCodeAsync 复核未遭 EIP-7702 委托（仅读数不一致亦暂停自转并告警），
    /// 再于 pending &gt; latest 持续超过 5 分钟（SponsorHeadWatch.StuckAfter）且无 Fueling 任务持有 latest 时，
    /// 在 AccountSlot 锁内预落库 Signed 尝试并广播 0-BNB 自转同 nonce 替换。状态持久化在 ColNetwork，跨 Pod 共享时间线。
    /// </summary>
    public virtual async Task RunSponsorHeadWatchdogAsync(
        ColNetwork network, IBscChainGateway gateway, CancellationToken ct)
    {
        if (MasterKey == null || string.IsNullOrEmpty(network.MnemonicCipher))
        {
            return;
        }

        using var mnemonic = HdKeyService.Unprotect(network.MnemonicCipher, MasterKey, _encryptor);
        var gasAddress = HdKeyService.DeriveAddress(mnemonic.Words, HdKeyService.GasAddressIndex).ToLowerInvariant();
        var (codeConfirmed, gasCode, codeDisagreement) = await gateway.VerifyDeployedCodeAsync(gasAddress, ct);
        if (codeConfirmed)
        {
            Logger.LogCritical(
                "{Prefix}Gas Sponsor {GasAddress} 检测到非空合约代码/EIP-7702 委托（code={Code}），已暂停看门狗自转",
                CollectionConsts.AlertPrefixGasLow, gasAddress, gasCode.Length > 24 ? gasCode[..24] + "..." : gasCode);
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixGasLow, "Gas 地址检测到 EIP-7702 委托/合约代码",
                $"Gas 地址 {gasAddress} eth_getCode 非空，看门狗已停止签署自转交易，请立即人工核查与更换归集助记词。");
            return;
        }

        if (codeDisagreement)
        {
            Logger.LogCritical(
                "{Prefix}Gas Sponsor {GasAddress} eth_getCode 多节点读数不一致（首次={Code}），已暂停看门狗自转等待核实",
                CollectionConsts.AlertPrefixGasLow, gasAddress, gasCode.Length > 24 ? gasCode[..24] + "..." : gasCode);
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixGasLow, "Gas 地址 eth_getCode 多节点读数不一致",
                $"Gas 地址 {gasAddress} eth_getCode 在不同节点响应不一致，看门狗暂不签署自转交易，请核查节点状态。");
            return;
        }

        var latest = await gateway.GetTransactionCountLatestAsync(gasAddress, ct);
        var pending = await gateway.GetTransactionCountAsync(gasAddress, ct);

        ColNetwork netRow;
        bool trackedHead;
        using (var readUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var readDb = await _dbContextProvider.GetDbContextAsync();
            netRow = await readDb.Networks.AsNoTracking().SingleAsync(n => n.Id == network.Id, ct);
            trackedHead = netRow.SponsorWatchHeadNonce == latest
                && await readDb.SweepTasks.AsNoTracking().AnyAsync(t =>
                    t.NetworkId == network.Id
                    && t.Status == SweepTaskStatus.Fueling
                    && t.GasTxHash != null
                    && t.GasNonceUsed == latest, ct);
            await readUow.CompleteAsync(ct);
        }

        var watch = SponsorHeadWatch.FromSnapshot(new SponsorHeadWatchSnapshot(
            netRow.SponsorWatchHeadNonce,
            netRow.SponsorWatchStuckSince,
            netRow.SponsorWatchLastSeenPendingAt,
            netRow.SponsorWatchLastReplaceAt,
            netRow.SponsorWatchReplaceAttempts,
            netRow.SponsorWatchGaveUp,
            netRow.SponsorWatchGaveUpAt));

        var now = _clock.Now;
        var action = watch.Decide(latest, pending, trackedHead, now);
        await SaveSponsorWatchSnapshotAsync(network.Id, watch.ToSnapshot(), ct);
        if (action == SponsorHeadAction.None)
        {
            return;
        }

        if (action == SponsorHeadAction.GiveUp)
        {
            Logger.LogError(
                "{Prefix}Gas Sponsor 队头看门狗连续 {Attempts} 次自转替换仍未推进（nonce={Nonce}, Sponsor={Sponsor}），暂停 1 小时后重试",
                CollectionConsts.AlertPrefixGasLow, watch.ReplaceAttempts, latest, gasAddress);
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixGasLow, "Gas 地址队头卡死看门狗已放弃本周期",
                $"Gas 地址 {gasAddress} nonce={latest} 连续 {watch.ReplaceAttempts} 次自转替换未生效，请检查 BNB 余额或链上 pending 交易。");
            return;
        }

        var gasKey = HdKeyService.DerivePrivateKey(mnemonic.Words, HdKeyService.GasAddressIndex);
        var networkGasWei = await gateway.GetGasPriceWeiAsync(ct);
        var desiredGasWei = BscGasPricePolicy.ReplacementGasWei(
            BscGasPricePolicy.ApplyBump(networkGasWei, watch.NextBumpSteps), networkGasWei);
        var signed = await gateway.PrepareTransactionAsync(
            gasKey, gasAddress, "", "0x0", network.ChainId, latest, desiredGasWei, ct);
        var txHash = signed.Hash.ToLowerInvariant();

        using (var lockUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var lockDb = await _dbContextProvider.GetDbContextAsync();
            await ChainTxSlotCoordinator.LockAccountSlotAsync(lockDb, network.Id, gasAddress, ct);
            var ownerAppeared = await lockDb.SweepTasks.AsNoTracking().AnyAsync(t =>
                t.NetworkId == network.Id
                && t.Status == SweepTaskStatus.Fueling
                && t.GasTxHash != null
                && t.GasNonceUsed == latest, ct);
            if (ownerAppeared)
            {
                return;
            }

            await InsertTxAttemptAsync(
                lockDb, network.Id, ChainTxPurpose.SponsorHeadSelfTransfer, refId: null,
                gasAddress, gasAddress, latest, watch.NextBumpSteps, txHash,
                desiredGasWei, BscGasPricePolicy.NativeTransferGasLimit, signed.RawHex,
                ChainTxAttemptStatus.Signed, now, ct);
            await lockUow.CompleteAsync(ct);
        }

        try
        {
            await gateway.BroadcastTransactionAsync(signed, ct);
            watch.RecordBroadcast(now);
            await SaveSponsorWatchSnapshotAsync(network.Id, watch.ToSnapshot(), ct);
            await UpdateTxAttemptStatusAsync(network.Id, txHash, ChainTxAttemptStatus.Broadcasted, clearRaw: false, ct);
            Logger.LogError(
                "{Prefix}Gas Sponsor 无主队头已用自转替换: Nonce={Nonce}, Attempt={Attempt}, GasPriceWei={GasWei}, Tx={Tx}",
                CollectionConsts.AlertPrefixGasLow, latest, watch.ReplaceAttempts, desiredGasWei, txHash);
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixGasLow, "Gas 地址无主队头已用自转替换",
                $"Gas 地址 {gasAddress} 无主卡池 nonce={latest}，已自动发送第 {watch.ReplaceAttempts} 次 0-BNB 自转替换（Tx={txHash}）。");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            watch.RecordRejected();
            await SaveSponsorWatchSnapshotAsync(network.Id, watch.ToSnapshot(), ct);
            if (ChainTxSlotCoordinator.IsDeterministicTxRejection(ex))
            {
                await UpdateTxAttemptStatusAsync(network.Id, txHash, ChainTxAttemptStatus.Rejected, clearRaw: true, ct);
            }
            Logger.LogWarning(ex,
                "Gas Sponsor 队头自转替换被拒或异常，下轮继续抬价: Nonce={Nonce}, Attempt={Attempt}, GasPriceWei={GasWei}",
                latest, watch.ReplaceAttempts, desiredGasWei);
        }
    }

    private async Task SaveSponsorWatchSnapshotAsync(
        Guid networkId, SponsorHeadWatchSnapshot snapshot, CancellationToken ct)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await _dbContextProvider.GetDbContextAsync();
        await db.Networks.Where(n => n.Id == networkId).ExecuteUpdateAsync(s => s
            .SetProperty(n => n.SponsorWatchHeadNonce, snapshot.HeadNonce)
            .SetProperty(n => n.SponsorWatchStuckSince, snapshot.StuckSince)
            .SetProperty(n => n.SponsorWatchLastSeenPendingAt, snapshot.LastSeenPendingAt)
            .SetProperty(n => n.SponsorWatchLastReplaceAt, snapshot.LastReplaceAt)
            .SetProperty(n => n.SponsorWatchReplaceAttempts, snapshot.ReplaceAttempts)
            .SetProperty(n => n.SponsorWatchGaveUp, snapshot.GaveUp)
            .SetProperty(n => n.SponsorWatchGaveUpAt, snapshot.GaveUpAt), ct);
        await uow.CompleteAsync(ct);
    }

    /// <summary>每种状态独立取批，回执先于新单。批次大小也约束同轮新激活和首播。</summary>
    public virtual async Task RunTokenRoundAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway,
        int confirmations, decimal? sweepRoundLimit, decimal fuelBnbLimit,
        TimeSpan retryCooldown, TimeSpan rebroadcastGrace, TimeSpan stuckThreshold,
        decimal gasBnbAlertThreshold,
        CancellationToken ct,
        int batchSize = ChainDefaults.SweepTaskBatchSize)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await using var changes = new ChainRecordChanges(dbContext, token.Symbol, _pushNotifier, Logger);
        var now = _clock.Now;

        // 调用方传入的网络来自另一个工作单元，重读后才能保存心跳和告警时间。
        network = await dbContext.Networks.SingleAsync(n => n.Id == network.Id, ct);
        network.SweepHeartbeatAt = now;

        batchSize = Math.Clamp(batchSize, 1, ChainDefaults.MaxSweepTaskBatchSize);
        var tasks = dbContext.SweepTasks.AsNoTracking().Where(t => t.TokenId == token.Id);
        long? roundHead = null;
        async Task<long> GetHeadAsync(CancellationToken tokenCt) =>
            roundHead ??= await gateway.GetLatestBlockNumberAsync(tokenCt);

        var sweeping = tasks.Where(t => t.Status == SweepTaskStatus.Sweeping);
        Task DriveSweepAsync(ColSweepTask task, CancellationToken taskCt) =>
            DriveSweepingAsync(network, token, gateway, dbContext, task, GetHeadAsync, confirmations, rebroadcastGrace, changes, taskCt);

        if (string.IsNullOrEmpty(token.VaultAddress))
        {
            // 停止签署新转账后仍核对已签名时保存的金库地址，以便已到账的资金完成结算。
            await RunStageAsync(dbContext, sweeping.Where(t => t.TxHash != null && t.BroadcastVaultAddress != null),
                batchSize, "已广播回执", DriveSweepAsync, changes, ct);
            await dbContext.SaveChangesAsync(ct);
            await uow.CompleteAsync(ct);
            return;
        }

        await RunStageAsync(dbContext, sweeping.Where(t => t.TxHash != null), batchSize, "回执", DriveSweepAsync, changes, ct);

        var retryTasks = await tasks.Where(t => t.Status == SweepTaskStatus.Failed && t.Retryable
                && t.StatusChangedAt <= now - retryCooldown)
            .OrderBy(t => t.StatusChangedAt).ThenBy(t => t.Id).Take(batchSize).ToListAsync(ct);
        var retryIds = retryTasks.Select(t => t.Id).ToArray();
        foreach (var task in retryTasks) changes.ObserveSweep(task);
        if (retryIds.Length > 0)
        {
            await dbContext.SweepTasks.Where(t => retryIds.Contains(t.Id) && t.Status == SweepTaskStatus.Failed && t.Retryable
                    && t.StatusChangedAt <= now - retryCooldown)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, SweepTaskStatus.Queued)
                    .SetProperty(t => t.Retryable, false)
                    .SetProperty(t => t.FailReason, (string?)null)
                    .SetProperty(t => t.StatusChangedAt, now), ct);
        }

        var queued = tasks.Where(t => t.Status == SweepTaskStatus.Queued && !dbContext.SweepTasks.Any(active =>
            active.AddressId == t.AddressId && (active.Status == SweepTaskStatus.Fueling || active.Status == SweepTaskStatus.Sweeping)));
        await RunStageAsync(dbContext, queued, batchSize, "排队", (task, taskCt) => TryActivateAsync(task, sweepRoundLimit, changes, taskCt), changes, ct);
        var resumedSweeps = new List<Guid>();
        await RunStageAsync(dbContext, tasks.Where(t => t.Status == SweepTaskStatus.Fueling), batchSize, "打油",
            (task, taskCt) => DriveFuelingAsync(network, token, gateway, dbContext, task, GetHeadAsync, confirmations,
                fuelBnbLimit, gasBnbAlertThreshold, resumedSweeps, changes, taskCt), changes, ct);
        await RunStageAsync(dbContext, sweeping.Where(t => t.TxHash == null || resumedSweeps.Contains(t.Id)),
            batchSize, "待广播", DriveSweepAsync, changes, ct);

        if (ChainAlertService.ShouldAlert(network.SweepStuckAlertedAt, TimeSpan.FromMinutes(60), now))
        {
            var stuck = await sweeping.Where(t => t.StatusChangedAt < now - stuckThreshold)
                .OrderBy(t => t.StatusChangedAt).ThenBy(t => t.Id).FirstOrDefaultAsync(ct);
            if (stuck != null)
            {
                await _alerts.RaiseAsync(CollectionConsts.AlertPrefixSweepStuck, "归集卡住",
                    $"代币 {token.Symbol} 任务 {stuck.Id}（地址 {stuck.Address}）在 Sweeping 停留超过 {stuckThreshold.TotalMinutes:0} 分钟（TxHash={stuck.TxHash ?? "未广播"}）。");
                network.SweepStuckAlertedAt = now;
            }
        }

        await dbContext.SaveChangesAsync(ct);
        await uow.CompleteAsync();
    }

    /// <summary>先记录本次处理时间再处理。失败或耗时的任务轮到队尾，其余阶段保留自己的时间预算。</summary>
    private async Task RunStageAsync(CollectionDbContext db, IQueryable<ColSweepTask> query, int batchSize,
        string stage, Func<ColSweepTask, CancellationToken, Task> processAsync, ChainRecordChanges changes, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(StageBudget);
        var stageCt = budget.Token;
        try
        {
            var batch = await query.OrderBy(t => t.LastProcessedAt ?? t.CreationTime)
                .ThenBy(t => t.CreationTime).ThenBy(t => t.Id).Take(batchSize).ToListAsync(stageCt);
            foreach (var task in batch)
            {
                var processedAt = _clock.Now;
                var rows = await db.SweepTasks.Where(t => t.Id == task.Id && t.Status == task.Status
                        && t.LastProcessedAt == task.LastProcessedAt)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.LastProcessedAt, processedAt), stageCt);
                if (rows == 0) continue;
                changes.ObserveSweep(task);
                try
                {
                    await processAsync(task, stageCt);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested)
                {
                    Logger.LogWarning(ex, "归集任务 {TaskId} 在{Stage}阶段处理异常，本轮跳过此任务", task.Id, stage);
                }
            }
        }
        catch (OperationCanceledException) when (stageCt.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            Logger.LogWarning("归集{Stage}阶段达到阶段时间上限，剩余任务留待下轮", stage);
        }
    }

    /// <summary>确定归集批次并转入 Fueling。已有同地址在途任务时等待下轮。</summary>
    private async Task TryActivateAsync(ColSweepTask candidate, decimal? roundLimit, ChainRecordChanges changes, CancellationToken ct)
    {
        using var activateUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var txDb = await _dbContextProvider.GetDbContextAsync();
        await DepositSweepQueue.LockAddressAsync(txDb, candidate.AddressId, ct);
        var task = await txDb.SweepTasks.AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == candidate.Id && t.Status == SweepTaskStatus.Queued, ct);
        if (task == null)
        {
            return;
        }

        var addressBusy = await txDb.SweepTasks
            .AnyAsync(t => t.AddressId == task.AddressId
                           && (t.Status == SweepTaskStatus.Fueling || t.Status == SweepTaskStatus.Sweeping), ct);
        if (addressBusy)
        {
            return;
        }

        var hasBroadcast = task.NonceUsed != null || task.TxHash != null || task.BroadcastAt != null;
        var linked = await txDb.DepositOrders.AsNoTracking()
            .Where(o => o.SweepTaskId == task.Id
                        && (o.Status == DepositOrderStatus.Sweeping
                            || o.Status == DepositOrderStatus.Confirmed))
            .OrderBy(o => o.CreationTime).ThenBy(o => o.Id)
            .ToListAsync(ct);
        var confirmedIds = linked.Where(o => o.Status == DepositOrderStatus.Confirmed).Select(o => o.Id).ToList();
        if (hasBroadcast) linked.RemoveAll(o => o.Status == DepositOrderStatus.Confirmed);
        if (linked.Count == 0)
        {
            await FailAsync(txDb, task.Id, SweepTaskStatus.Queued, retryable: false,
                reason: "归集排队任务无可处理的关联进款，请核对单据", _clock.Now);
            await activateUow.CompleteAsync(ct);
            return;
        }

        // 对重试任务保留已激活的进款集合，已确认失败并清掉广播证据也不改变批次。
        var hasActivatedOrders = linked.Any(o => o.Status == DepositOrderStatus.Sweeping);
        var keepBatch = hasBroadcast || hasActivatedOrders;
        if (hasActivatedOrders)
        {
            linked.RemoveAll(o => o.Status != DepositOrderStatus.Sweeping);
        }

        // 新批次至少选一笔，避免单笔金额超过上限后永远排队。
        decimal sum = 0m;
        var pickedIds = new List<Guid>();
        for (var i = 0; i < linked.Count; i++)
        {
            var value = linked[i].Amount ?? 0m;
            if (!keepBatch && i > 0 && roundLimit.HasValue && sum + value > roundLimit.Value)
            {
                break;
            }

            pickedIds.Add(linked[i].Id);
            sum += value;
        }

        if (keepBatch && sum != task.Amount)
        {
            await FailAsync(txDb, task.Id, SweepTaskStatus.Queued, retryable: false,
                reason: "原归集批次金额与关联进款不一致，请核对单据", _clock.Now);
            await activateUow.CompleteAsync(ct);
            return;
        }

        var now = _clock.Now;

        // 占用任务、标记入选单据和解绑其余单据必须一起提交，确保入账集合与待广播金额一致。
        int claimed;
        try
        {
            claimed = await txDb.SweepTasks
                .Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Queued)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Amount, sum)
                    .SetProperty(t => t.Status, SweepTaskStatus.Fueling)
                    .SetProperty(t => t.StatusChangedAt, now), ct);
        }
        catch (PostgresException ex) when (ex.SqlState == "23505")
        {
            // 地址的在途状态受唯一索引保护；冲突时当前激活事务回滚。
            Logger.LogWarning("归集任务 {TaskId} 激活时地址已有在途任务，本轮跳过", task.Id);
            return;
        }
        if (claimed == 0)
        {
            return;
        }

        var updated = await txDb.DepositOrders
            .Where(o => o.SweepTaskId == task.Id && pickedIds.Contains(o.Id)
                        && (o.Status == DepositOrderStatus.Confirmed || o.Status == DepositOrderStatus.Sweeping))
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, DepositOrderStatus.Sweeping), ct);
        if (updated != pickedIds.Count)
        {
            return;
        }

        // 未入选单据继续保持 Confirmed，由扫链轮加入后续任务。
        await txDb.DepositOrders
            .Where(o => o.SweepTaskId == task.Id && o.Status == DepositOrderStatus.Confirmed
                        && !pickedIds.Contains(o.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.SweepTaskId, (Guid?)null), ct);

        await activateUow.CompleteAsync(ct);
        foreach (var id in confirmedIds) changes.DepositChanged(id);
    }

    /// <summary>
    /// 驱动 Fueling 任务：在广播前以 AccountSlot 锁串行化同网 Gas Sponsor，预落库 Signed 尝试与 nonce；
    /// 已在途打油按同 nonce 全候选查回执，队头卡池 ≥ 3 分钟（SponsorHeadReplaceAfter）按 +10%+1 wei 同 nonce 替换；
    /// 打油前统一按 RequiredSweepGasWei 校验与计算差额补油，并经 balanceOf + eth_call 模拟及有界 discover 拦截无效打油；
    /// 估算打油超过 FuelBnbLimit 时保持 Fueling 延后等待 gas 回落。
    /// </summary>
    private async Task DriveFuelingAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway, CollectionDbContext dbContext,
        ColSweepTask task, Func<CancellationToken, Task<long>> getHeadAsync, int confirmations,
        decimal fuelBnbLimit, decimal gasBnbAlertThreshold, List<Guid> resumedSweeps,
        ChainRecordChanges changes, CancellationToken ct)
    {
        var now = _clock.Now;

        // 1. 先检查已有打油交易回执（查同 nonce 槽位全部候选 Attempt + task.GasTxHash）
        var gasNonce = task.GasNonceUsed;
        long? minedGasBlock = null;
        var fuelAttempts = gasNonce.HasValue
            ? await ChainTxSlotCoordinator.LoadSlotAttemptsAsync(dbContext, network.Id, ChainTxPurpose.SweepFuel, task.Id, gasNonce.Value, ct)
            : new List<ColChainTxAttempt>();

        if (task.GasTxHash != null)
        {
            var (winningHash, gasReceipt) = await FindFuelSlotReceiptAsync(gateway, task.GasTxHash, fuelAttempts, ct);
            if (gasReceipt != null)
            {
                var winningAttempt = fuelAttempts.FirstOrDefault(a =>
                    string.Equals(a.TxHash, winningHash, StringComparison.OrdinalIgnoreCase));
                var fallbackGasWei = winningAttempt?.GasPriceWei ?? task.GasTxGasPriceWei;

                if (gasReceipt.Status == 0)
                {
                    if (gasReceipt.BlockNumber is long block
                        && await getHeadAsync(ct) - block >= confirmations)
                    {
                        using (var settleUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
                        {
                            var settleDb = await _dbContextProvider.GetDbContextAsync();
                            await ChainTxSlotCoordinator.MarkSlotSettledAsync(
                                settleDb, network.Id, ChainTxPurpose.SweepFuel, task.Id, gasNonce,
                                winningHash!, ChainTxAttemptStatus.Reverted, ct);
                            await settleUow.CompleteAsync(ct);
                        }
                        await TryRecordSweepGasFeeLogAsync(network, ChainTxPurpose.SweepFuel, task.Id, winningHash!, gasReceipt, fallbackGasWei, ct);
                        await dbContext.SweepTasks.Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling
                                && t.GasTxHash == task.GasTxHash && t.GasNonceUsed == gasNonce)
                            .ExecuteUpdateAsync(s => s.SetProperty(t => t.GasTxHash, (string?)null)
                                .SetProperty(t => t.GasSignedTransaction, (string?)null)
                                .SetProperty(t => t.GasNonceUsed, (long?)null)
                                .SetProperty(t => t.GasBroadcastAt, (DateTime?)null)
                                .SetProperty(t => t.GasTxGasPriceWei, (string?)null)
                                .SetProperty(t => t.FuelBumpSteps, task.FuelBumpSteps + 1), ct);
                    }
                    return;
                }

                if (gasReceipt.Status != 1 || gasReceipt.BlockNumber is not (>= 0 and var mined)) return;
                minedGasBlock = mined;
                using (var settleUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
                {
                    var settleDb = await _dbContextProvider.GetDbContextAsync();
                    await ChainTxSlotCoordinator.MarkSlotSettledAsync(
                        settleDb, network.Id, ChainTxPurpose.SweepFuel, task.Id, gasNonce,
                        winningHash!, ChainTxAttemptStatus.Confirmed, ct);
                    if (!string.Equals(winningHash, task.GasTxHash, StringComparison.OrdinalIgnoreCase))
                    {
                        await settleDb.SweepTasks.Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling
                                && t.GasTxHash == task.GasTxHash && t.GasNonceUsed == gasNonce)
                            .ExecuteUpdateAsync(s => s.SetProperty(t => t.GasTxHash, winningHash), ct);
                        task.GasTxHash = winningHash;
                    }
                    await settleUow.CompleteAsync(ct);
                }
                await TryRecordSweepGasFeeLogAsync(network, ChainTxPurpose.SweepFuel, task.Id, winningHash!, gasReceipt, fallbackGasWei, ct);
            }
        }

        // 2. 无打油交易且无归集交易时：先检查代币余额 + eth_call 预模拟（不足则有界 discover），拦截无效打油
        if (task.GasTxHash == null && task.TxHash == null)
        {
            var preflight = await TryPreflightTokenAndDiscoverAsync(
                network, token, gateway, dbContext, task, getHeadAsync, confirmations, changes, ct);
            if (preflight != PreflightOutcome.Proceed)
            {
                return;
            }
        }

        var networkGasWei = await gateway.GetGasPriceWeiAsync(ct);
        var addressBnb = await gateway.GetBnbBalanceAsync(task.Address, ct);
        var addressBnbWei = BscGasPricePolicy.EtherToWei(addressBnb);
        var isSweepReplacement = task.TxHash != null;
        var prevSweepGasWei = await ResolvePreviousSweepGasWeiAsync(gateway, task, ct);
        var sweepDesiredGasWei = BscGasPricePolicy.RequiredSweepGasWei(
            networkGasWei, task.GasBumpSteps, prevSweepGasWei, isSweepReplacement);
        var requiredSweepGasWei = sweepDesiredGasWei * BscGasPricePolicy.Erc20TransferGasLimit;
        if (addressBnbWei >= requiredSweepGasWei)
        {
            var rows = await dbContext.SweepTasks.Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, SweepTaskStatus.Sweeping)
                    .SetProperty(t => t.GasSignedTransaction, minedGasBlock.HasValue ? null : task.GasSignedTransaction)
                    .SetProperty(t => t.StatusChangedAt, now), ct);
            if (rows > 0 && task.TxHash != null)
            {
                resumedSweeps.Add(task.Id);
            }
            return;
        }

        if (minedGasBlock is long gasBlock)
        {
            // 已成功打油后仍不足以归集，等待最终确认再转人工，避免自动重复给同一地址转 BNB。
            if (await getHeadAsync(ct) - gasBlock >= confirmations)
                await FailAsync(dbContext, task.Id, SweepTaskStatus.Fueling, retryable: false,
                    reason: "打油交易已确认，但地址余额仍不足以支付归集费用，请核对", now);
            return;
        }

        // 3. 已有在途打油交易（尚无回执）
        if (task.GasTxHash != null)
        {
            if (MasterKey != null && !string.IsNullOrEmpty(network.MnemonicCipher) && task.GasNonceUsed is long trackedGasNonce)
            {
                using var trackedMnemonic = HdKeyService.Unprotect(network.MnemonicCipher, MasterKey, _encryptor);
                var trackedGasAddress = HdKeyService.DeriveAddress(trackedMnemonic.Words, HdKeyService.GasAddressIndex).ToLowerInvariant();
                var latestNonce = await gateway.GetTransactionCountLatestAsync(trackedGasAddress, ct);
                if (trackedGasNonce < latestNonce)
                {
                    var confirmedBlock = Math.Max(0, await getHeadAsync(ct) - confirmations);
                    var confirmedCount = await gateway.GetTransactionCountAtBlockAsync(trackedGasAddress, confirmedBlock, ct);
                    if (confirmedCount.HasValue && confirmedCount.Value > trackedGasNonce)
                    {
                        using (var replaceClearUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
                        {
                            var replaceClearDb = await _dbContextProvider.GetDbContextAsync();
                            await ChainTxSlotCoordinator.MarkSlotSettledAsync(
                                replaceClearDb, network.Id, ChainTxPurpose.SweepFuel, task.Id, trackedGasNonce,
                                string.Empty, ChainTxAttemptStatus.Replaced, ct);
                            await replaceClearDb.SweepTasks.Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling
                                    && t.GasTxHash == task.GasTxHash && t.GasNonceUsed == trackedGasNonce)
                                .ExecuteUpdateAsync(s => s.SetProperty(t => t.GasTxHash, (string?)null)
                                    .SetProperty(t => t.GasSignedTransaction, (string?)null)
                                    .SetProperty(t => t.GasNonceUsed, (long?)null)
                                    .SetProperty(t => t.GasBroadcastAt, (DateTime?)null)
                                    .SetProperty(t => t.GasTxGasPriceWei, (string?)null)
                                    .SetProperty(t => t.FuelBumpSteps, task.FuelBumpSteps + 1), ct);
                            await replaceClearUow.CompleteAsync(ct);
                        }
                        return;
                    }
                }

                if (trackedGasNonce == latestNonce
                    && task.GasBroadcastAt.HasValue
                    && now - task.GasBroadcastAt.Value >= SponsorHeadReplaceAfter)
                {
                    var (replaceFuelAmount, replaceValueWei) = ComputeDeltaFuelBudget(
                        networkGasWei, sweepDesiredGasWei, addressBnbWei);
                    if (replaceFuelAmount > fuelBnbLimit)
                    {
                        await RaiseFuelLimitDeferAlertAsync(network, dbContext, replaceFuelAmount, fuelBnbLimit, now, ct);
                        return;
                    }

                    var sponsorBal = await gateway.GetBnbBalanceAsync(trackedGasAddress, ct);
                    if (sponsorBal < replaceFuelAmount)
                    {
                        await RaiseGasBalanceLowAlertAsync(network, gasBnbAlertThreshold, trackedGasAddress, sponsorBal, now);
                        return;
                    }

                    var maxAttemptNo = fuelAttempts.Count > 0
                        ? fuelAttempts.Max(a => a.AttemptNo)
                        : task.FuelBumpSteps + 1;
                    var nextFuelBump = Math.Max(task.FuelBumpSteps + 1, maxAttemptNo);
                    var prevGasWei = BigInteger.Max(
                        BscGasPricePolicy.ParseWeiOrZero(task.GasTxGasPriceWei),
                        (await gateway.GetTransactionGasPriceWeiAsync(task.GasTxHash, ct)) ?? BigInteger.Zero);
                    foreach (var attempt in fuelAttempts)
                    {
                        var aWei = BscGasPricePolicy.ParseWeiOrZero(attempt.GasPriceWei);
                        if (aWei > prevGasWei)
                        {
                            prevGasWei = aWei;
                        }
                    }

                    var replaceGasWei = BscGasPricePolicy.ReplacementGasWei(
                        BscGasPricePolicy.ApplyBump(networkGasWei, nextFuelBump), prevGasWei);
                    var gasKey = HdKeyService.DerivePrivateKey(trackedMnemonic.Words, HdKeyService.GasAddressIndex);
                    var replacement = await gateway.PrepareTransactionAsync(
                        gasKey, task.Address, "", replaceValueWei, network.ChainId, trackedGasNonce, replaceGasWei, ct);
                    var oldGasHash = task.GasTxHash;
                    var replaceHash = replacement.Hash.ToLowerInvariant();

                    // 先只持久化新候选 Attempt（Status=Signed），绝不在广播确认前覆写 GasTxHash/GasBroadcastAt；
                    // 这样若替换被节点拒收，旧 GasTxHash 与冷却计时均保持准确且下轮可立刻重抬档。
                    bool reserved;
                    using (var replaceUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
                    {
                        var replaceDb = await _dbContextProvider.GetDbContextAsync();
                        await ChainTxSlotCoordinator.LockAccountSlotAsync(replaceDb, task.NetworkId, trackedGasAddress, ct);
                        var stillOwned = await replaceDb.SweepTasks.AsNoTracking().AnyAsync(t =>
                            t.Id == task.Id && t.Status == SweepTaskStatus.Fueling
                            && t.GasTxHash == oldGasHash && t.GasNonceUsed == trackedGasNonce, ct);
                        reserved = stillOwned;
                        if (stillOwned)
                        {
                            await InsertTxAttemptAsync(
                                replaceDb, task.NetworkId, ChainTxPurpose.SweepFuel, task.Id,
                                trackedGasAddress, task.Address.ToLowerInvariant(), trackedGasNonce,
                                maxAttemptNo + 1, replaceHash, replaceGasWei,
                                BscGasPricePolicy.NativeTransferGasLimit, replacement.RawHex,
                                ChainTxAttemptStatus.Signed, now, ct);
                        }
                        await replaceUow.CompleteAsync(ct);
                    }

                    if (!reserved)
                    {
                        return;
                    }

                    try
                    {
                        await gateway.BroadcastTransactionAsync(replacement, ct);
                        using var commitUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                        var commitDb = await _dbContextProvider.GetDbContextAsync();
                        await ChainTxSlotCoordinator.LockAccountSlotAsync(commitDb, task.NetworkId, trackedGasAddress, ct);
                        var replacedRows = await commitDb.SweepTasks.Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling
                                && t.GasTxHash == oldGasHash && t.GasNonceUsed == trackedGasNonce)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(t => t.GasTxHash, replaceHash)
                                .SetProperty(t => t.GasSignedTransaction, replacement.RawHex)
                                .SetProperty(t => t.GasBroadcastAt, now)
                                .SetProperty(t => t.FuelBumpSteps, nextFuelBump)
                                .SetProperty(t => t.GasTxGasPriceWei, replaceGasWei.ToString(CultureInfo.InvariantCulture)), ct);
                        if (replacedRows > 0)
                        {
                            await MarkAttemptStatusInDbAsync(commitDb, task.NetworkId, replaceHash, ChainTxAttemptStatus.Broadcasted, clearRaw: false, ct);
                            await MarkAttemptStatusInDbAsync(commitDb, task.NetworkId, oldGasHash, ChainTxAttemptStatus.Replaced, clearRaw: true, ct);
                        }
                        await commitUow.CompleteAsync(ct);
                        Logger.LogWarning(
                            "归集任务 {TaskId} 打油队头超时，已同 nonce={Nonce} 抬价替换（{OldHash} -> {NewHash}, GasPriceWei={GasWei}）",
                            task.Id, trackedGasNonce, oldGasHash, replaceHash, replaceGasWei);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        if (ChainTxSlotCoordinator.IsDeterministicTxRejection(ex))
                        {
                            await UpdateTxAttemptStatusAsync(task.NetworkId, replaceHash, ChainTxAttemptStatus.Rejected, clearRaw: true, ct);
                        }
                        Logger.LogWarning(ex, "归集任务 {TaskId} 打油替换广播异常（{Hash}），保留原队头计时以便下轮立即重抬价", task.Id, replaceHash);
                    }
                    return;
                }
            }

            if (task.GasSignedTransaction != null && await gateway.GetTransactionNonceAsync(task.GasTxHash, ct) == null)
                await gateway.BroadcastTransactionAsync(new(task.GasTxHash, task.GasSignedTransaction), ct);
            return;
        }

        // 4. 全新打油交易（先算差额预算、再检查全局 Sponsor 串行、再签名落库广播）
        var (fuelAmount, valueWei) = ComputeDeltaFuelBudget(networkGasWei, sweepDesiredGasWei, addressBnbWei);
        if (fuelAmount > fuelBnbLimit)
        {
            // gas 暴涨超过打油上限属于链况暂时性问题：留在 Fueling 延后恢复，避免永久 Failed 须人工逐笔点重试
            await RaiseFuelLimitDeferAlertAsync(network, dbContext, fuelAmount, fuelBnbLimit, now, ct);
            return;
        }

        // Gas Sponsor 全网严格单笔在途串行，避免负载均衡节点 pending 漂移导致互相阻塞
        var sponsorBusy = await dbContext.SweepTasks.AsNoTracking().AnyAsync(t =>
            t.NetworkId == task.NetworkId
            && t.Id != task.Id
            && t.Status == SweepTaskStatus.Fueling
            && (t.GasTxHash != null || t.GasNonceUsed != null), ct);
        if (sponsorBusy)
        {
            Logger.LogInformation("Gas Sponsor 已有在途打油任务，归集任务 {TaskId} 本轮排队等待", task.Id);
            return;
        }

        if (MasterKey == null)
        {
            await _alerts.RaiseAsync(null, "归集不可用",
                $"归集任务 {task.Id} 缺少 Collection:MasterKey，无法为充值地址签署打油交易。");
            return;
        }

        using var mnemonic = HdKeyService.Unprotect(network.MnemonicCipher!, MasterKey, _encryptor);
        var gasAddress = HdKeyService.DeriveAddress(mnemonic.Words, HdKeyService.GasAddressIndex).ToLowerInvariant();
        if (gasNonce.HasValue)
        {
            var nextPending = await gateway.GetTransactionCountAsync(gasAddress, ct);
            if (nextPending > gasNonce.Value)
            {
                // pending 只表示可能在途。只有足够深的区块也已消耗该 nonce，才结束无哈希旧任务的自动重试。
                var confirmedBlock = Math.Max(0, await getHeadAsync(ct) - confirmations);
                if (await gateway.GetTransactionCountAtBlockAsync(gasAddress, confirmedBlock, ct) > gasNonce.Value)
                    await FailAsync(dbContext, task.Id, SweepTaskStatus.Fueling, retryable: false,
                        reason: "历史打油交易缺少哈希，原 nonce 已确认使用且地址余额不足，请人工核对", now);
                return;
            }
        }

        var latestFuelNonce = await gateway.GetTransactionCountLatestAsync(gasAddress, ct);
        var gasBalance = await gateway.GetBnbBalanceAsync(gasAddress, ct);
        await RaiseGasBalanceLowAlertAsync(network, gasBnbAlertThreshold, gasAddress, gasBalance, now);

        if (gasBalance < fuelAmount)
        {
            // Gas 不足：保持 Fueling 等运维补币（下轮再试），不置 Failed（可再试类）
            Logger.LogWarning("Gas 地址余额 {Balance} BNB 不足以打油 {Fuel}，任务 {TaskId} 等待补币",
                gasBalance, fuelAmount, task.Id);
            return;
        }

        var gasKeyForNew = HdKeyService.DerivePrivateKey(mnemonic.Words, HdKeyService.GasAddressIndex);
        var nonce = gasNonce ?? latestFuelNonce;
        // 另一任务已占用该 nonce 时重播原签名，避免将同一笔 BNB 改付给其他地址。
        var reservedTask = await dbContext.SweepTasks.AsNoTracking().Where(t => t.NetworkId == task.NetworkId
                && t.Id != task.Id && t.GasNonceUsed == nonce && t.GasSignedTransaction != null && t.GasTxHash != null)
            .Select(t => new SignedChainTransaction(t.GasTxHash!, t.GasSignedTransaction!, null, null)).FirstOrDefaultAsync(ct);
        if (reservedTask != null)
        {
            await gateway.BroadcastTransactionAsync(reservedTask, ct);
            return;
        }

        var fuelGasWei = BscGasPricePolicy.ApplyBump(networkGasWei, task.FuelBumpSteps);
        var signed = await gateway.PrepareTransactionAsync(
            gasKeyForNew, task.Address, "", valueWei, network.ChainId, nonce, fuelGasWei, ct);
        var signedHash = signed.Hash.ToLowerInvariant();
        var broadcastAt = _clock.Now;

        using (var gasUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var gasDb = await _dbContextProvider.GetDbContextAsync();
            await ChainTxSlotCoordinator.LockAccountSlotAsync(gasDb, task.NetworkId, gasAddress, ct);
            var anotherInFlight = await gasDb.SweepTasks.AnyAsync(t => t.NetworkId == task.NetworkId
                && t.Id != task.Id
                && t.Status == SweepTaskStatus.Fueling
                && (t.GasTxHash != null || t.GasNonceUsed != null), ct);
            if (anotherInFlight)
            {
                return;
            }

            var unresolved = await gasDb.SweepTasks.AnyAsync(t => t.NetworkId == task.NetworkId
                && t.Id != task.Id && t.GasNonceUsed == nonce
                && (t.Status == SweepTaskStatus.Fueling || t.GasTxHash == null || t.GasSignedTransaction != null), ct);
            if (unresolved)
            {
                return;
            }

            var claimed = await gasDb.SweepTasks.Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling
                    && t.GasTxHash == null && t.GasNonceUsed == gasNonce)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.GasNonceUsed, nonce)
                    .SetProperty(t => t.GasTxHash, signedHash)
                    .SetProperty(t => t.GasSignedTransaction, signed.RawHex)
                    .SetProperty(t => t.GasBroadcastAt, broadcastAt)
                    .SetProperty(t => t.GasTxGasPriceWei, fuelGasWei.ToString(CultureInfo.InvariantCulture)), ct);
            if (claimed == 0)
            {
                return;
            }

            await InsertTxAttemptAsync(
                gasDb, task.NetworkId, ChainTxPurpose.SweepFuel, task.Id,
                gasAddress, task.Address.ToLowerInvariant(), nonce,
                task.FuelBumpSteps + 1, signedHash, fuelGasWei,
                BscGasPricePolicy.NativeTransferGasLimit, signed.RawHex,
                ChainTxAttemptStatus.Signed, broadcastAt, ct);
            await gasUow.CompleteAsync(ct);
        }

        try
        {
            await gateway.BroadcastTransactionAsync(signed, ct);
            await UpdateTxAttemptStatusAsync(task.NetworkId, signedHash, ChainTxAttemptStatus.Broadcasted, clearRaw: false, ct);
            Logger.LogInformation("归集任务 {TaskId} 已提交 {Fuel} BNB 打油交易 {Hash}（Nonce={Nonce}, GasWei={GasWei}），等待回执",
                task.Id, fuelAmount, signedHash, nonce, fuelGasWei);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ChainTxSlotCoordinator.IsDeterministicTxRejection(ex))
        {
            using var revertUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var revertDb = await _dbContextProvider.GetDbContextAsync();
            await MarkAttemptStatusInDbAsync(revertDb, task.NetworkId, signedHash, ChainTxAttemptStatus.Rejected, clearRaw: true, ct);
            await revertDb.SweepTasks.Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling
                    && t.GasTxHash == signedHash && t.GasNonceUsed == nonce)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.GasTxHash, (string?)null)
                    .SetProperty(t => t.GasSignedTransaction, (string?)null)
                    .SetProperty(t => t.GasNonceUsed, (long?)null)
                    .SetProperty(t => t.GasBroadcastAt, (DateTime?)null)
                    .SetProperty(t => t.GasTxGasPriceWei, (string?)null)
                    .SetProperty(t => t.FuelBumpSteps, task.FuelBumpSteps + 1), ct);
            await revertUow.CompleteAsync(ct);
            Logger.LogWarning(ex, "归集任务 {TaskId} 打油首播被节点确定性拒收（{Hash}），已释放占位下轮抬档重签", task.Id, signedHash);
        }
    }

    private async Task<(string? WinningHash, TransactionReceipt? Receipt)> FindFuelSlotReceiptAsync(
        IBscChainGateway gateway, string primaryHash, List<ColChainTxAttempt> attempts, CancellationToken ct)
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (seen.Add(primaryHash))
        {
            candidates.Add(primaryHash);
        }

        foreach (var attempt in attempts)
        {
            if (!string.IsNullOrEmpty(attempt.TxHash) && seen.Add(attempt.TxHash))
            {
                candidates.Add(attempt.TxHash);
            }
        }

        foreach (var candidateHash in candidates)
        {
            var receipt = await gateway.GetTransactionReceiptAsync(candidateHash, ct);
            if (!string.Equals(receipt.TxHash, candidateHash, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (receipt.Status is 0 or 1)
            {
                return (candidateHash.ToLowerInvariant(), receipt);
            }
        }

        return (null, null);
    }

    private async Task<BigInteger> ResolvePreviousSweepGasWeiAsync(
        IBscChainGateway gateway, ColSweepTask task, CancellationToken ct)
    {
        if (task.TxHash == null)
        {
            return BigInteger.Zero;
        }

        return BigInteger.Max(
            BscGasPricePolicy.ParseWeiOrZero(task.SweepTxGasPriceWei),
            (await gateway.GetTransactionGasPriceWeiAsync(task.TxHash, ct)) ?? BigInteger.Zero);
    }

    private static (decimal FuelBnb, string ValueWeiHex) ComputeDeltaFuelBudget(
        BigInteger networkGasWei, BigInteger requiredSweepGasWei, BigInteger currentAddressBnbWei)
    {
        var minWei = BscGasPricePolicy.EtherToWei(MinimumFuelBnb);
        var fuelWei = BscGasPricePolicy.ComputeDeltaFuelWei(
            networkGasWei, requiredSweepGasWei, currentAddressBnbWei, minWei);
        var fuelAmount = BscGasPricePolicy.WeiToEther(fuelWei);
        var valueWei = "0x" + (fuelWei > 0 ? fuelWei.ToString("x") : "0");
        return (fuelAmount, valueWei);
    }

    private async Task RaiseGasBalanceLowAlertAsync(
        ColNetwork network, decimal gasBnbAlertThreshold, string gasAddress, decimal gasBalance, DateTime now)
    {
        if (gasBalance < gasBnbAlertThreshold
            && ChainAlertService.ShouldAlert(network.GasLowAlertedAt, TimeSpan.FromMinutes(60), now))
        {
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixGasLow, "Gas 地址 BNB 过低",
                $"Gas 地址 {gasAddress} 余额 {gasBalance:0.000000} BNB，已低于预警阈值 {gasBnbAlertThreshold} BNB，请补充。");
            network.GasLowAlertedAt = now;
        }
    }

    private async Task RaiseFuelLimitDeferAlertAsync(
        ColNetwork network, CollectionDbContext dbContext, decimal fuelAmount, decimal fuelBnbLimit, DateTime now, CancellationToken ct)
    {
        Logger.LogError("打油金额 {Fuel:0.000000} BNB 超过上限 {Max}，归集打油本轮延后等待 gas 回落", fuelAmount, fuelBnbLimit);
        if (ChainAlertService.ShouldAlert(network.GasLowAlertedAt, TimeSpan.FromMinutes(60), now))
        {
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixGasLow, "Gas 价格偏高打油延后",
                $"当前预估打油金额 {fuelAmount:0.000000} BNB 超过上限 {fuelBnbLimit} BNB，归集任务已暂缓打油，待 gas 回落后自动恢复。");
            network.GasLowAlertedAt = now;
            await dbContext.Networks.Where(n => n.Id == network.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.GasLowAlertedAt, now), ct);
        }
    }

    /// <summary>
    /// 驱动 Sweeping 任务：轮询全部候选归集哈希回执；超过宽限未出块时复用原 nonce 按 RequiredSweepGasWei 抬价重签（先落库 Signed 再广播）；
    /// 地址 BNB 不足期望抬价时禁止静默压价（超 2 分钟打油平衡同步宽限后退回 Fueling 补油）。任一候选通过确认与转账核对即可完成入账。
    /// </summary>
    private async Task DriveSweepingAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway, CollectionDbContext dbContext,
        ColSweepTask task, Func<CancellationToken, Task<long>> getHeadAsync, int confirmations,
        TimeSpan rebroadcastGrace, ChainRecordChanges changes, CancellationToken ct)
    {
        var now = _clock.Now;
        var head = task.TxHash == null ? 0 : await getHeadAsync(ct);
        var (receipt, uncertain, transferMismatch) = await FindSweepReceiptAsync(
            dbContext, gateway, task, token, head, confirmations, ct);
        if (receipt == null)
        {
            if (string.IsNullOrEmpty(token.VaultAddress)) return;
            if (transferMismatch && !uncertain)
            {
                await FailSweepAttemptAsync(dbContext, task, "归集回执未包含与本单匹配的代币转账，拒绝入账", now, ct);
                return;
            }

            // 无 nonce 的历史哈希只能继续查回执。上次发送结果不明时保留完整等待窗口。
            if (uncertain || (task.TxHash != null && task.NonceUsed == null)
                || (task.BroadcastAt.HasValue && now - task.BroadcastAt.Value <= rebroadcastGrace))
            {
                return;
            }

            // 补齐旧批次的状态时以任务金额为上限，防止仍关联的多余单据变成待入账。
            var sweepingGross = await dbContext.DepositOrders.AsNoTracking()
                .Where(o => o.SweepTaskId == task.Id && o.Status == DepositOrderStatus.Sweeping)
                .SumAsync(o => (decimal?)o.Amount ?? 0m, ct);
            var promotableBudget = task.Amount - sweepingGross;
            if (promotableBudget > 0m)
            {
                var candidates = await dbContext.DepositOrders.AsNoTracking()
                    .Where(o => o.SweepTaskId == task.Id && o.Status == DepositOrderStatus.Confirmed)
                    .OrderBy(o => o.CreationTime)
                    .Select(o => new { o.Id, Amount = (decimal?)o.Amount ?? 0m })
                    .ToListAsync(ct);
                var promotable = new List<Guid>();
                decimal acc = 0m;
                foreach (var c in candidates)
                {
                    if (acc + c.Amount > promotableBudget) break;
                    promotable.Add(c.Id);
                    acc += c.Amount;
                }

                if (promotable.Count > 0)
                {
                    var promoted = await dbContext.Database.SqlQuery<Guid>($"""
                        WITH updated AS (UPDATE "ColDepositOrders" SET "Status" = {(int)DepositOrderStatus.Sweeping}
                            WHERE "SweepTaskId" = {task.Id} AND "Status" = {(int)DepositOrderStatus.Confirmed}
                              AND "Id" = ANY({promotable.ToArray()}) RETURNING "Id")
                        SELECT "Id" AS "Value" FROM updated
                        """).ToListAsync(ct);
                    foreach (var id in promoted) changes.DepositChanged(id);
                }
            }

            var linkable = await dbContext.DepositOrders.AsNoTracking()
                .Where(o => o.SweepTaskId == task.Id && o.Status == DepositOrderStatus.Sweeping)
                .ToListAsync(ct);
            if (linkable.Count == 0 || linkable.Sum(o => o.Amount ?? 0m) != task.Amount)
            {
                // 没有关联进款可结算，停在失败状态等待核对。
                await FailAsync(dbContext, task.Id, SweepTaskStatus.Sweeping, retryable: false,
                    reason: "关联进款毛额与归集任务不一致，拒绝广播", now);
                return;
            }

            var vaultAddress = task.BroadcastVaultAddress ?? token.VaultAddress!;
            string dataHex;
            try
            {
                dataHex = BuildErc20TransferData(vaultAddress, task.Amount, token.Decimals);
            }
            catch (OverflowException)
            {
                // 合计金额超出 decimal 可换算范围（10 位整数上限的单不该聚到一起）：仅人工
                await FailAsync(dbContext, task.Id, SweepTaskStatus.Sweeping, retryable: false,
                    reason: $"任务金额 {task.Amount} 超出可换算范围", now);
                return;
            }

            // 首笔未广播时再次做 balanceOf + eth_call 预模拟（覆盖地址自带 BNB 绕过 Fueling 的任务）
            if (task.TxHash == null)
            {
                var preflight = await TryPreflightTokenAndDiscoverAsync(
                    network, token, gateway, dbContext, task, getHeadAsync, confirmations, changes, ct);
                if (preflight != PreflightOutcome.Proceed)
                {
                    return;
                }
            }

            var isReplacement = task.TxHash != null;
            var nextBumpSteps = isReplacement ? task.GasBumpSteps + 1 : task.GasBumpSteps;
            var networkGasWei = await gateway.GetGasPriceWeiAsync(ct);
            var prevGasWei = await ResolvePreviousSweepGasWeiAsync(gateway, task, ct);
            var desiredGasWei = BscGasPricePolicy.RequiredSweepGasWei(
                networkGasWei, task.GasBumpSteps, prevGasWei, isReplacement);

            var addressBnb = await gateway.GetBnbBalanceAsync(task.Address, ct);
            var addressBnbWei = BscGasPricePolicy.EtherToWei(addressBnb);
            var affordableGasWei = BscGasPricePolicy.CapToAffordable(
                desiredGasWei, addressBnbWei, BscGasPricePolicy.Erc20TransferGasLimit);
            if (affordableGasWei <= 0 || desiredGasWei > affordableGasWei)
            {
                // 刚打完油读到余额不足可能是 RPC 视图滞后：宽限内等待下轮重读，禁止双打油
                if (!isReplacement && task.GasTxHash != null
                    && task.GasBroadcastAt.HasValue && now - task.GasBroadcastAt.Value < FuelSettleGrace)
                {
                    Logger.LogWarning(
                        "归集任务 {TaskId} 地址 BNB 读数不足但打油刚落库（GasTx={GasTx}），等待节点余额同步",
                        task.Id, task.GasTxHash);
                    return;
                }

                // 禁止静默压价广播（会导致重复哈希或长期卡池）：退回 Fueling 补油；
                // 不提前递增 GasBumpSteps（只保留已签名档位），避免 Fueling ↔ Sweeping 档位漂移与乒乓。
                Logger.LogWarning(
                    "归集任务 {TaskId} 地址 BNB（{Bnb}）不足以支付抬价后 gas（DesiredWei={DesiredWei}），退回打油（CommittedBumpSteps={BumpSteps}）",
                    task.Id, addressBnb, desiredGasWei, task.GasBumpSteps);
                await MatchSweepAttempt(dbContext, task).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, SweepTaskStatus.Fueling)
                    .SetProperty(t => t.StatusChangedAt, now)
                    .SetProperty(t => t.GasTxHash, (string?)null)
                    .SetProperty(t => t.GasSignedTransaction, (string?)null)
                    .SetProperty(t => t.GasNonceUsed, (long?)null)
                    .SetProperty(t => t.GasBroadcastAt, (DateTime?)null)
                    .SetProperty(t => t.GasTxGasPriceWei, (string?)null), ct);
                return;
            }

            if (MasterKey == null)
            {
                await _alerts.RaiseAsync(null, "归集不可用", "Collection:MasterKey 未配置，无法签名归集交易。");
                return;
            }

            using var mnemonic = HdKeyService.Unprotect(network.MnemonicCipher!, MasterKey, _encryptor);
            var userKey = HdKeyService.DerivePrivateKey(mnemonic.Words, task.AddressIndex);
            var nonce = task.NonceUsed ?? await gateway.GetTransactionCountLatestAsync(task.Address, ct);
            var transaction = await gateway.PrepareTransactionAsync(
                userKey, token.ContractAddress, dataHex, "0x0", network.ChainId, nonce, desiredGasWei, ct);
            var newTxHash = transaction.Hash.ToLowerInvariant();
            var previousHashes = SweepHashes(task)
                .Where(hash => !string.Equals(hash, newTxHash, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var claimedAt = _clock.Now;
            int claimed;
            using (var broadcastUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                var broadcastDb = await _dbContextProvider.GetDbContextAsync();
                claimed = await MatchSweepAttempt(broadcastDb, task)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.TxHash, newTxHash)
                        .SetProperty(t => t.PreviousTxHashes, previousHashes.Length == 0 ? null : previousHashes)
                        .SetProperty(t => t.BroadcastAt, claimedAt)
                        .SetProperty(t => t.BroadcastVaultAddress, vaultAddress)
                        .SetProperty(t => t.NonceUsed, nonce)
                        .SetProperty(t => t.GasBumpSteps, nextBumpSteps)
                        .SetProperty(t => t.SweepTxGasPriceWei, desiredGasWei.ToString(CultureInfo.InvariantCulture)), ct);
                if (claimed > 0)
                {
                    if (isReplacement && task.TxHash != null)
                    {
                        await MarkAttemptStatusInDbAsync(broadcastDb, task.NetworkId, task.TxHash, ChainTxAttemptStatus.Replaced, clearRaw: true, ct);
                    }

                    await InsertTxAttemptAsync(
                        broadcastDb, task.NetworkId, ChainTxPurpose.SweepTransfer, task.Id,
                        task.Address.ToLowerInvariant(), token.ContractAddress.ToLowerInvariant(), nonce,
                        nextBumpSteps + 1, newTxHash, desiredGasWei,
                        BscGasPricePolicy.Erc20TransferGasLimit, transaction.RawHex,
                        ChainTxAttemptStatus.Signed, claimedAt, ct);
                }
                await broadcastUow.CompleteAsync(ct);
            }
            if (claimed == 0)
            {
                return;
            }

            await gateway.BroadcastTransactionAsync(transaction, ct);
            await UpdateTxAttemptStatusAsync(task.NetworkId, newTxHash, ChainTxAttemptStatus.Broadcasted, clearRaw: false, ct);
            Logger.LogInformation("任务 {TaskId} 归集已广播 {Amount} {Symbol}（{Hash}, Nonce={Nonce}, BumpSteps={BumpSteps}, GasWei={GasWei}）",
                task.Id, task.Amount, token.Symbol, newTxHash, nonce, nextBumpSteps, desiredGasWei);
            return;
        }

        // 回执可能来自首次签名或后续替换，结算采用确认通过的哈希。
        if (receipt.Status is not (0 or 1))
        {
            return;
        }

        if (receipt.BlockNumber is not long receiptBlock)
        {
            return;
        }

        if (receipt.Status == 0)
        {
            // 只有已达到确认深度的失败回执才允许人工重试使用新 nonce。
            if (head - receiptBlock < confirmations)
            {
                return;
            }
            await UpdateTxAttemptStatusAsync(task.NetworkId, receipt.TxHash, ChainTxAttemptStatus.Reverted, clearRaw: true, ct);
            await TryRecordSweepGasFeeLogAsync(network, ChainTxPurpose.SweepTransfer, task.Id, receipt.TxHash, receipt, task.SweepTxGasPriceWei, ct, task.Address);
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixTokenPaused, "代币暂停或转账被拒",
                $"代币 {token.Symbol} 归集交易 {receipt.TxHash} 链上失败（revert），请人工核查合约状态。");
            await MatchSweepAttempt(dbContext, task).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, SweepTaskStatus.Failed)
                .SetProperty(t => t.Retryable, false)
                .SetProperty(t => t.FailReason, "归集交易链上 revert，已确认未转账")
                .SetProperty(t => t.StatusChangedAt, now)
                .SetProperty(t => t.TxHash, (string?)null)
                .SetProperty(t => t.PreviousTxHashes, (string[]?)null)
                .SetProperty(t => t.NonceUsed, (long?)null)
                .SetProperty(t => t.BroadcastAt, (DateTime?)null)
                .SetProperty(t => t.BroadcastVaultAddress, (string?)null)
                .SetProperty(t => t.TxBlockNumber, (long?)null)
                .SetProperty(t => t.SweepTxGasPriceWei, (string?)null), ct);
            return;
        }

        // 回执在块里：确认数不够 → 保持 Sweeping 和原哈希，下轮再看（不增加替换档、不重签）
        await MatchSweepAttempt(dbContext, task)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.TxBlockNumber, receiptBlock), ct);
        if (head - receiptBlock < confirmations)
        {
            return;
        }

        var winningHash = receipt.TxHash.ToLowerInvariant();
        await TryRecordSweepGasFeeLogAsync(network, ChainTxPurpose.SweepTransfer, task.Id, winningHash, receipt, task.SweepTxGasPriceWei, ct, task.Address);
        await SettleSweepTaskAsync(dbContext, network, token, task, winningHash, receiptBlock, vaultOverride: null, now, changes, ct);
    }

    private enum PreflightOutcome
    {
        Proceed = 0,
        Settled = 1,
        Defer = 2,
        Failed = 3,
    }

    private enum DiscoverOutcome
    {
        NotFound = 0,
        Found = 1,
        Deferred = 2,
    }

    /// <summary>
    /// 签发打油或首笔归集前，在安全链头校验充值地址代币余额并做 eth_call 模拟（安全头低于进款块时暂缓等待同步）：
    /// 1) 余额充足但 eth_call 确定 revert → 转人工失败并告警，不烧 BNB；模拟未决则本轮暂缓；
    /// 2) 余额不足 → 经有界 nonce 预检与分块 getLogs（每轮最多 DiscoverMaxCallsPerRound=16 次）发现已确认外部归集；
    ///    连续 PreflightDeficitThreshold=3 轮仍不足且未发现归集，才转人工失败。
    /// </summary>
    private async Task<PreflightOutcome> TryPreflightTokenAndDiscoverAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway, CollectionDbContext dbContext,
        ColSweepTask task, Func<CancellationToken, Task<long>> getHeadAsync, int confirmations,
        ChainRecordChanges changes, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token.VaultAddress))
        {
            return PreflightOutcome.Proceed;
        }

        var vaultAddress = task.BroadcastVaultAddress ?? token.VaultAddress!;
        var linked = await dbContext.DepositOrders.AsNoTracking()
            .Where(o => o.SweepTaskId == task.Id
                        && (o.Status == DepositOrderStatus.Sweeping || o.Status == DepositOrderStatus.Confirmed))
            .OrderBy(o => o.CreationTime).ThenBy(o => o.Id)
            .ToListAsync(ct);
        if (linked.Count == 0)
        {
            // 无关联进款由各自阶段的完整性门禁报错（如 zero-linked 失败文案），不在预检阶段掩盖
            return PreflightOutcome.Proceed;
        }

        var preflightHead = await getHeadAsync(ct);
        var safeHead = Math.Max(0, preflightHead - confirmations);
        var maxDepositBlock = linked.Max(o => o.BlockNumber);
        if (maxDepositBlock > 0 && safeHead < maxDepositBlock)
        {
            Logger.LogDebug(
                "归集任务 {TaskId} 安全链头 {SafeHead} 尚低于进款块 {DepositBlock}（负载节点视图滞后），本轮暂缓",
                task.Id, safeHead, maxDepositBlock);
            return PreflightOutcome.Defer;
        }

        string rawBalance;
        if (safeHead > 0)
        {
            try
            {
                rawBalance = await gateway.GetTokenBalanceRawAtBlockAsync(token.ContractAddress, task.Address, safeHead, ct);
            }
            catch (ChainRpcException)
            {
                rawBalance = await gateway.GetTokenBalanceRawAsync(token.ContractAddress, task.Address, ct);
            }
        }
        else
        {
            rawBalance = await gateway.GetTokenBalanceRawAsync(token.ContractAddress, task.Address, ct);
        }

        var balance = DepositScanManager.TryScaleRaw(rawBalance, token.Decimals, out var overflow);
        if (overflow || balance == null)
        {
            await FailAsync(dbContext, task.Id, task.Status, retryable: false,
                reason: $"充值地址 {token.Symbol} 余额溢出无法换算，请人工核对", _clock.Now);
            return PreflightOutcome.Failed;
        }

        if (balance.Value >= task.Amount)
        {
            if (task.PreflightDeficitCount > 0)
            {
                await dbContext.SweepTasks.Where(t => t.Id == task.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.PreflightDeficitCount, 0), ct);
                task.PreflightDeficitCount = 0;
            }

            string dataHex;
            try
            {
                dataHex = BuildErc20TransferData(vaultAddress, task.Amount, token.Decimals);
            }
            catch (OverflowException)
            {
                return PreflightOutcome.Proceed;
            }

            var sim = await gateway.SimulateCallAsync(task.Address, token.ContractAddress, dataHex, ct);
            if (sim.Reverted)
            {
                await _alerts.RaiseAsync(CollectionConsts.AlertPrefixTokenPaused, "归集模拟转账被拒",
                    $"代币 {token.Symbol} 任务 {task.Id}（地址 {task.Address}）eth_call 模拟失败：{sim.ErrorMessage ?? "execution reverted"}，已拦截打油。");
                await FailAsync(dbContext, task.Id, task.Status, retryable: false,
                    reason: $"归集前 eth_call 模拟转账失败（{sim.ErrorMessage ?? "revert"}），已拦截打油", _clock.Now);
                return PreflightOutcome.Failed;
            }

            if (!sim.Success)
            {
                Logger.LogWarning("归集任务 {TaskId} eth_call 模拟未决（{Error}），本轮暂缓", task.Id, sim.ErrorMessage);
                return PreflightOutcome.Defer;
            }

            return PreflightOutcome.Proceed;
        }

        // 余额不足：先做有界链上发现；确认无匹配转出时需连续多轮验证（防负载均衡读滞后单次误杀）才转人工失败
        var discovered = await TryDiscoverCompletedSweepAsync(
            network, token, gateway, dbContext, task, linked, vaultAddress, getHeadAsync, confirmations, changes, ct);
        if (discovered == DiscoverOutcome.Found)
        {
            return PreflightOutcome.Settled;
        }
        if (discovered == DiscoverOutcome.Deferred)
        {
            return PreflightOutcome.Defer;
        }

        var nextDeficit = task.PreflightDeficitCount + 1;
        await dbContext.SweepTasks.Where(t => t.Id == task.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.PreflightDeficitCount, nextDeficit), ct);
        task.PreflightDeficitCount = nextDeficit;

        if (nextDeficit < PreflightDeficitThreshold)
        {
            Logger.LogWarning(
                "归集任务 {TaskId} 安全块 {SafeHead} 余额（{Balance}）低于任务额（{Amount}），第 {Count}/{Max} 次暂缓待多轮复核",
                task.Id, safeHead, balance.Value, task.Amount, nextDeficit, PreflightDeficitThreshold);
            return PreflightOutcome.Defer;
        }

        await FailAsync(dbContext, task.Id, task.Status, retryable: false,
            reason: $"充值地址 {token.Symbol} 余额（{balance.Value}）连续 {nextDeficit} 轮不足任务金额（{task.Amount}），且链上未发现匹配归集交易，已拦截打油",
            _clock.Now);
        return PreflightOutcome.Failed;
    }

    private async Task<DiscoverOutcome> TryDiscoverCompletedSweepAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway, CollectionDbContext dbContext,
        ColSweepTask task, List<ColDepositOrder> linked, string vaultAddress,
        Func<CancellationToken, Task<long>> getHeadAsync, int confirmations, ChainRecordChanges changes, CancellationToken ct)
    {
        var minDepositBlock = linked.Min(o => o.BlockNumber);
        if (minDepositBlock <= 0)
        {
            return DiscoverOutcome.NotFound;
        }

        // 同址其他任务已使用过的所有归集哈希集合（防同一笔外部归集被两个任务重复认领）
        var otherTasks = await dbContext.SweepTasks.AsNoTracking()
            .Where(t => t.AddressId == task.AddressId && t.Id != task.Id && (t.TxHash != null || t.PreviousTxHashes != null))
            .Select(t => new { t.TxHash, t.PreviousTxHashes })
            .ToListAsync(ct);
        var otherTaskUsedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in otherTasks)
        {
            if (!string.IsNullOrEmpty(row.TxHash))
            {
                otherTaskUsedHashes.Add(row.TxHash);
            }
            if (row.PreviousTxHashes != null)
            {
                foreach (var h in row.PreviousTxHashes)
                {
                    if (!string.IsNullOrEmpty(h))
                    {
                        otherTaskUsedHashes.Add(h);
                    }
                }
            }
        }

        // Nonce 预检：只统计已确认/已入账（Swept）任务消耗的去重链上 nonce 数，
        // 绝不能把未上链的 PreviousTxHashes 替换哈希计入分母，否则替换过的地址会永久跳过外部归集发现。
        var sweptNonces = await dbContext.SweepTasks.AsNoTracking()
            .Where(t => t.AddressId == task.AddressId && t.Status == SweepTaskStatus.Swept)
            .Select(t => t.NonceUsed)
            .ToListAsync(ct);
        var confirmedNonceCount = sweptNonces.Where(n => n.HasValue).Select(n => n!.Value).Distinct().Count()
            + sweptNonces.Count(n => !n.HasValue);
        var latestNonce = await gateway.GetTransactionCountLatestAsync(task.Address, ct);
        if (latestNonce <= confirmedNonceCount)
        {
            return DiscoverOutcome.NotFound;
        }

        var head = await getHeadAsync(ct);
        var safeHead = head - confirmations;
        var startBlock = Math.Max(minDepositBlock, task.DiscoverCursorBlock ?? minDepositBlock);
        if (safeHead < startBlock)
        {
            return DiscoverOutcome.NotFound;
        }

        var callsUsed = 0;
        var chunkFrom = startBlock;
        var currentSpan = DiscoverBlockChunkSpan;
        while (chunkFrom <= safeHead && callsUsed < DiscoverMaxCallsPerRound)
        {
            var chunkTo = Math.Min(safeHead, chunkFrom + currentSpan - 1);
            callsUsed++;
            var (logs, complete) = await gateway.GetFilteredTransferLogsAsync(
                token.ContractAddress, task.Address, vaultAddress, chunkFrom, chunkTo, ct);
            if (!complete && chunkTo > chunkFrom)
            {
                currentSpan = Math.Max(1, (chunkTo - chunkFrom + 1) / 2);
                continue;
            }
            if (!complete)
            {
                // 单块仍不完整，保存游标等下轮
                await dbContext.SweepTasks.Where(t => t.Id == task.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.DiscoverCursorBlock, chunkFrom), ct);
                return DiscoverOutcome.Deferred;
            }

            var matched = logs
                .Where(l => l.BlockNumber >= minDepositBlock
                            && l.BlockNumber <= safeHead
                            && !otherTaskUsedHashes.Contains(l.TxHash)
                            && string.Equals(l.From, task.Address, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(l.To, vaultAddress, StringComparison.OrdinalIgnoreCase)
                            && DepositScanManager.TryScaleRaw(l.RawAmount, token.Decimals, out var ov) == task.Amount
                            && !ov)
                .OrderBy(l => l.BlockNumber).ThenBy(l => l.LogIndex)
                .FirstOrDefault();
            if (matched != null)
            {
                var receipt = await gateway.GetTransactionReceiptAsync(matched.TxHash, ct);
                if (receipt.Status == 1
                    && receipt.BlockNumber == matched.BlockNumber
                    && head - matched.BlockNumber >= confirmations)
                {
                    var (receiptLogs, receiptComplete) = await gateway.GetReceiptTransferLogsAsync(
                        matched.TxHash, token.ContractAddress, matched.BlockNumber, ct);
                    var hasProof = receiptComplete && receiptLogs.Any(l =>
                        l.BlockNumber == matched.BlockNumber
                        && string.Equals(l.TxHash, matched.TxHash, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(l.From, task.Address, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(l.To, vaultAddress, StringComparison.OrdinalIgnoreCase)
                        && DepositScanManager.TryScaleRaw(l.RawAmount, token.Decimals, out _) == task.Amount);
                    if (hasProof)
                    {
                        var settled = await SettleSweepTaskAsync(
                            dbContext, network, token, task, matched.TxHash.ToLowerInvariant(),
                            matched.BlockNumber, vaultAddress, _clock.Now, changes, ct);
                        if (settled)
                        {
                            Logger.LogInformation(
                                "归集任务 {TaskId} 链上发现并结算已归集交易 {TxHash}（Block={Block}, Amount={Amount} {Symbol}）",
                                task.Id, matched.TxHash, matched.BlockNumber, task.Amount, token.Symbol);
                            return DiscoverOutcome.Found;
                        }
                    }
                }
            }

            chunkFrom = chunkTo + 1;
            currentSpan = DiscoverBlockChunkSpan;
        }

        if (chunkFrom <= safeHead)
        {
            await dbContext.SweepTasks.Where(t => t.Id == task.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.DiscoverCursorBlock, chunkFrom), ct);
            return DiscoverOutcome.Deferred;
        }

        return DiscoverOutcome.NotFound;
    }

    private async Task<bool> SettleSweepTaskAsync(
        CollectionDbContext dbContext, ColNetwork network, ColToken token,
        ColSweepTask task, string winningHash, long receiptBlock, string? vaultOverride,
        DateTime now, ChainRecordChanges changes, CancellationToken ct)
    {
        // 确认总转账与关联单据毛额一致后，任务、余额、凭证和单据状态才一同提交。
        using var creditUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var creditDbContext = await _dbContextProvider.GetDbContextAsync();
        var linked = await creditDbContext.DepositOrders
            .Where(o => o.SweepTaskId == task.Id && o.Status == DepositOrderStatus.Sweeping)
            .ToListAsync(ct);
        var linkedGross = linked.Sum(order => order.Amount ?? 0m);
        if (linked.Count == 0 || linkedGross != task.Amount)
        {
            await MatchSweepTaskExact(creditDbContext, task).ExecuteUpdateAsync(s => s
                .SetProperty(row => row.Status, SweepTaskStatus.Failed)
                .SetProperty(row => row.Retryable, false)
                .SetProperty(row => row.FailReason, $"链接单毛额合计 {linkedGross} 与广播金额 {task.Amount} 不一致，拒绝入账待人工核对")
                .SetProperty(row => row.StatusChangedAt, now), ct);
            await creditUow.CompleteAsync(ct);
            await _alerts.RaiseAsync(null, "归集卡住",
                $"任务 {task.Id} 链上确认但链接单毛额合计 {linkedGross} ≠ 广播金额 {task.Amount}，拒绝自动入账");
            return false;
        }

        var vaultToSave = vaultOverride ?? task.BroadcastVaultAddress ?? token.VaultAddress;
        var rows = await MatchSweepTaskExact(creditDbContext, task)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, SweepTaskStatus.Swept)
                .SetProperty(t => t.TxHash, winningHash)
                .SetProperty(t => t.PreviousTxHashes, (string[]?)null)
                .SetProperty(t => t.BroadcastVaultAddress, vaultToSave)
                .SetProperty(t => t.TxBlockNumber, receiptBlock)
                .SetProperty(t => t.StatusChangedAt, now), ct);
        if (rows == 0)
        {
            return false;
        }

        await MarkSweepWinningAttemptAsync(creditDbContext, task.NetworkId, task.Id, winningHash, ct);

        foreach (var order in linked)
        {
            // 用户余额记净额。账本分别记毛额和手续费，用户负债的净变动与余额一致。
            var gross = order.Amount ?? 0m;
            var credit = CollectionLedgerPoster.ResolveCreditAmount(
                order.Amount, order.ActualAmount, order.Fee);
            var currency = token.GetBalanceCurrency();
            await _balanceManager.CreditAsync(
                order.UserId, currency, credit,
                idempotencyKey: BalanceIdempotencyKeys.ForDepositOrder(order.Id));
            await _ledgerPoster.PostDepositCreditAsync(order.Id, order.UserId, currency, gross);
            await _ledgerPoster.PostDepositFeeAsync(order.Id, order.UserId, currency, order.Fee ?? 0m);
            order.Status = DepositOrderStatus.Swept;
        }

        // 解绑旧批次里残留的 Confirmed 单据，供下轮重新挂队。
        var detached = await creditDbContext.Database.SqlQuery<Guid>($"""
            WITH updated AS (UPDATE "ColDepositOrders" SET "SweepTaskId" = NULL
                WHERE "SweepTaskId" = {task.Id} AND "Status" = {(int)DepositOrderStatus.Confirmed} RETURNING "Id")
            SELECT "Id" AS "Value" FROM updated
            """).ToListAsync(ct);

        await creditUow.CompleteAsync(ct);
        foreach (var order in linked) changes.DepositChanged(order.Id);
        foreach (var id in detached) changes.DepositChanged(id);
        Logger.LogInformation("归集任务 {TaskId} 确认毛额 {GrossAmount} {Symbol}，已结算 {Count} 笔进款",
            task.Id, task.Amount, token.Symbol, linked.Count);
        return true;
    }

    /// <summary>坏回执只影响对应哈希，旧候选继续核对。缺少可靠查单结果时保持原 nonce 和候选。</summary>
    private async Task<(TransactionReceipt? Receipt, bool Uncertain, bool TransferMismatch)> FindSweepReceiptAsync(
        CollectionDbContext db, IBscChainGateway gateway, ColSweepTask task, ColToken token, long head, int confirmations, CancellationToken ct)
    {
        TransactionReceipt? unconfirmed = null;
        var uncertain = false;
        var transferMismatch = false;
        var candidates = SweepHashes(task).ToArray();
        for (var offset = 0; offset < candidates.Length; offset++)
        {
            var index = (int)(((long)task.ReceiptCursor + offset) % candidates.Length);
            var hash = candidates[index];
            if (candidates.Length > 1)
                await MatchSweepAttempt(db, task).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.ReceiptCursor, (index + 1) % candidates.Length), ct);
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
            budget.CancelAfter(ReceiptQueryBudget);
            try
            {
                var receipt = await gateway.GetTransactionReceiptAsync(hash, budget.Token);
                if (!string.Equals(hash, receipt.TxHash, StringComparison.OrdinalIgnoreCase))
                {
                    uncertain = true;
                    continue;
                }
                if (receipt.Status == null && receipt.BlockNumber == null) continue;
                if (receipt.Status is not (0 or 1) || receipt.BlockNumber is not (>= 0 and var block) || block > head)
                {
                    uncertain = true;
                    continue;
                }
                if (head - block < confirmations)
                {
                    unconfirmed ??= receipt;
                    continue;
                }
                if (receipt.Status == 0) return (receipt, uncertain, transferMismatch);

                var (logs, complete) = await gateway.GetReceiptTransferLogsAsync(hash, token.ContractAddress, block, budget.Token);
                if (!complete)
                {
                    uncertain = true;
                    continue;
                }
                if (logs.Any(log => log.BlockNumber == block
                    && string.Equals(log.TxHash, hash, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(log.From, task.Address, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(log.To, task.BroadcastVaultAddress ?? token.VaultAddress, StringComparison.OrdinalIgnoreCase)
                    && DepositScanManager.TryScaleRaw(log.RawAmount, token.Decimals, out _) == task.Amount))
                {
                    return (receipt, uncertain, transferMismatch);
                }
                transferMismatch = true;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested &&
                (ex is ChainRpcException or System.Net.Http.HttpRequestException or OperationCanceledException))
            {
                uncertain = true;
                Logger.LogWarning("任务 {TaskId} 的候选交易 {Hash} 回执查询失败（{Type}），继续核对其他候选",
                    task.Id, hash, ex.GetType().Name);
            }
        }

        return (unconfirmed, uncertain, transferMismatch);
    }

    private async Task InsertTxAttemptAsync(
        CollectionDbContext db, Guid networkId, ChainTxPurpose purpose, Guid? refId,
        string senderAddress, string toAddress, long nonce, int attemptNo, string txHash,
        BigInteger gasPriceWei, int gasLimit, string? rawSignedTx, ChainTxAttemptStatus status,
        DateTime broadcastAt, CancellationToken ct)
    {
        var normalizedHash = txHash.ToLowerInvariant();
        if (await db.ChainTxAttempts.AsNoTracking().AnyAsync(a => a.NetworkId == networkId && a.TxHash == normalizedHash, ct))
        {
            return;
        }

        var id = LazyServiceProvider != null ? GuidGenerator.Create() : Guid.NewGuid();
        db.ChainTxAttempts.Add(new ColChainTxAttempt(id)
        {
            NetworkId = networkId,
            Purpose = purpose,
            RefId = refId,
            SenderAddress = senderAddress.ToLowerInvariant(),
            ToAddress = toAddress.ToLowerInvariant(),
            Nonce = nonce,
            AttemptNo = attemptNo,
            TxHash = normalizedHash,
            GasPriceWei = gasPriceWei.ToString(CultureInfo.InvariantCulture),
            GasLimit = gasLimit,
            RawSignedTx = rawSignedTx,
            Status = status,
            BroadcastAt = broadcastAt,
            CreationTime = broadcastAt,
        });
        await db.SaveChangesAsync(ct);
    }

    private async Task UpdateTxAttemptStatusAsync(
        Guid networkId, string txHash, ChainTxAttemptStatus status, bool clearRaw, CancellationToken ct)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await _dbContextProvider.GetDbContextAsync();
        await MarkAttemptStatusInDbAsync(db, networkId, txHash, status, clearRaw, ct);
        await uow.CompleteAsync(ct);
    }

    private static Task MarkAttemptStatusInDbAsync(
        CollectionDbContext db, Guid networkId, string txHash, ChainTxAttemptStatus status, bool clearRaw, CancellationToken ct)
    {
        var normalized = txHash.ToLowerInvariant();
        var query = db.ChainTxAttempts.Where(a => a.NetworkId == networkId && a.TxHash == normalized);
        return clearRaw
            ? query.ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, status)
                .SetProperty(a => a.RawSignedTx, (string?)null), ct)
            : query.ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, status), ct);
    }

    private Task TryRecordSweepGasFeeLogAsync(
        ColNetwork network, ChainTxPurpose purpose, Guid taskId, string txHash,
        TransactionReceipt receipt, string? fallbackGasPriceWei, CancellationToken ct,
        string? explicitSenderAddress = null)
    {
        string? sender = explicitSenderAddress;
        if (sender == null && purpose == ChainTxPurpose.SweepFuel
            && MasterKey != null && !string.IsNullOrEmpty(network.MnemonicCipher))
        {
            using var mnemonic = HdKeyService.Unprotect(network.MnemonicCipher, MasterKey, _encryptor);
            sender = HdKeyService.DeriveAddress(mnemonic.Words, HdKeyService.GasAddressIndex).ToLowerInvariant();
        }

        return ChainGasFeeRecorder.TryRecordAsync(
            _unitOfWorkManager, _dbContextProvider, _clock,
            network.Id, purpose, taskId, txHash, receipt, fallbackGasPriceWei, sender, ct);
    }

    private static async Task MarkSweepWinningAttemptAsync(
        CollectionDbContext db, Guid networkId, Guid taskId, string winningHash, CancellationToken ct)
    {
        var normalized = winningHash.ToLowerInvariant();
        await db.ChainTxAttempts
            .Where(a => a.NetworkId == networkId && a.Purpose == ChainTxPurpose.SweepTransfer
                        && a.RefId == taskId && a.TxHash == normalized)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, ChainTxAttemptStatus.Confirmed)
                .SetProperty(a => a.RawSignedTx, (string?)null), ct);
        await db.ChainTxAttempts
            .Where(a => a.NetworkId == networkId && a.Purpose == ChainTxPurpose.SweepTransfer
                        && a.RefId == taskId && a.TxHash != normalized
                        && (a.Status == ChainTxAttemptStatus.Signed || a.Status == ChainTxAttemptStatus.Broadcasted))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, ChainTxAttemptStatus.Replaced)
                .SetProperty(a => a.RawSignedTx, (string?)null), ct);
    }

    private static IEnumerable<string> SweepHashes(ColSweepTask task)
    {
        if (task.TxHash != null)
        {
            yield return task.TxHash;
        }

        foreach (var hash in task.PreviousTxHashes ?? [])
        {
            yield return hash;
        }
    }

    private static IQueryable<ColSweepTask> MatchSweepAttempt(CollectionDbContext db, ColSweepTask task) =>
        db.SweepTasks.Where(row => row.Id == task.Id && row.Status == SweepTaskStatus.Sweeping
            && row.TxHash == task.TxHash && row.NonceUsed == task.NonceUsed && row.BroadcastAt == task.BroadcastAt);

    private static IQueryable<ColSweepTask> MatchSweepTaskExact(CollectionDbContext db, ColSweepTask task) =>
        db.SweepTasks.Where(row => row.Id == task.Id && row.Status == task.Status
            && row.TxHash == task.TxHash && row.NonceUsed == task.NonceUsed && row.BroadcastAt == task.BroadcastAt);

    private static Task FailSweepAttemptAsync(CollectionDbContext db, ColSweepTask task, string reason,
        DateTime now, CancellationToken ct) =>
        MatchSweepAttempt(db, task).ExecuteUpdateAsync(s => s
            .SetProperty(row => row.Status, SweepTaskStatus.Failed)
            .SetProperty(row => row.Retryable, false)
            .SetProperty(row => row.FailReason, reason)
            .SetProperty(row => row.StatusChangedAt, now), ct);

    private static Task FailAsync(CollectionDbContext dbContext, Guid taskId, SweepTaskStatus from,
        bool retryable, string reason, DateTime now)
    {
        return dbContext.SweepTasks
            .Where(t => t.Id == taskId && t.Status == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, SweepTaskStatus.Failed)
                .SetProperty(t => t.Retryable, retryable)
                .SetProperty(t => t.FailReason, reason)
                .SetProperty(t => t.StatusChangedAt, now));
    }

    /// <summary>ERC20 transfer(address,uint256) calldata：0xa9059cbb + pad32(to) + pad32(amount)。</summary>
    internal static string BuildErc20TransferData(string to, decimal amount, int decimals)
    {
        return "0xa9059cbb"
               + to.ToLowerInvariant().TrimStart('0', 'x').PadLeft(64, '0')
               + ToRawHex(amount, decimals).PadLeft(64, '0');
    }

    /// <summary>decimal（token 单位）→ 原始整数（10^decimals 单位）的十六进制数字串（无 0x 前缀）。</summary>
    internal static string ToRawHex(decimal amount, int decimals)
    {
        // checked：金额 × 10^18 溢出 decimal（约 7.9e10 token）时抛 OverflowException，
        // 由调用方转「仅人工」失败，绝不带着错误金额上链
        var scaled = checked(decimal.Round(amount * Pow10(decimals), 0));
        var raw = scaled.ToString(CultureInfo.InvariantCulture);
        return raw == "0" ? "" : BigInteger.Parse(raw).ToString("x");
    }

    private static decimal Pow10(int n)
    {
        decimal r = 1;
        for (var i = 0; i < n; i++)
        {
            r *= 10;
        }

        return r;
    }
}
