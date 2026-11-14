using AbpAdmin.Collection.Ledger;
using System;
using System.Collections.Generic;
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
/// 归集代币前先落库签名哈希和 nonce，避免节点收到交易但响应丢失后用新 nonce 重发。
/// 任务状态按原尝试条件更新。确认与转账核对通过后，任务完结、余额和凭证在同一事务提交。
/// </summary>
public class SweepManager : DomainService
{
    private static readonly TimeSpan StageBudget = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan ReceiptQueryBudget = TimeSpan.FromSeconds(3);
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly CollectionBalanceManager _balanceManager;
    private readonly CollectionLedgerPoster _ledgerPoster;
    private readonly ChainAlertService _alerts;
    private readonly IStringEncryptionService _encryptor;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public SweepManager(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        CollectionBalanceManager balanceManager,
        CollectionLedgerPoster ledgerPoster,
        ChainAlertService alerts,
        IStringEncryptionService encryptor,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _dbContextProvider = dbContextProvider;
        _balanceManager = balanceManager;
        _ledgerPoster = ledgerPoster;
        _alerts = alerts;
        _encryptor = encryptor;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
    }

    private string? MasterKey => _masterKey;
    private string? _masterKey;

    /// <summary>用于解密归集密钥的主密钥，由 worker 组装。为空时无法执行签名。</summary>
    public SweepManager WithMasterKey(string? masterKey)
    {
        _masterKey = masterKey;
        return this;
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
        if (string.IsNullOrEmpty(token.VaultAddress))
        {
            Logger.LogWarning("代币 {Symbol} 未配置金库地址，归集轮空转（Confirmed 单保持待归集）", token.Symbol);
            return;
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
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
            DriveSweepingAsync(network, token, gateway, dbContext, task, GetHeadAsync, confirmations, rebroadcastGrace, taskCt);

        await RunStageAsync(dbContext, sweeping.Where(t => t.TxHash != null), batchSize, "回执", DriveSweepAsync, ct);

        var retryIds = tasks.Where(t => t.Status == SweepTaskStatus.Failed && t.Retryable
                && t.StatusChangedAt <= now - retryCooldown)
            .OrderBy(t => t.StatusChangedAt).ThenBy(t => t.Id).Take(batchSize).Select(t => t.Id);
        await dbContext.SweepTasks.Where(t => retryIds.Contains(t.Id) && t.Status == SweepTaskStatus.Failed && t.Retryable)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, SweepTaskStatus.Queued)
                .SetProperty(t => t.Retryable, false)
                .SetProperty(t => t.FailReason, (string?)null)
                .SetProperty(t => t.StatusChangedAt, now), ct);

        var queued = tasks.Where(t => t.Status == SweepTaskStatus.Queued && !dbContext.SweepTasks.Any(active =>
            active.AddressId == t.AddressId && (active.Status == SweepTaskStatus.Fueling || active.Status == SweepTaskStatus.Sweeping)));
        await RunStageAsync(dbContext, queued, batchSize, "排队", (task, taskCt) => TryActivateAsync(task, sweepRoundLimit, taskCt), ct);
        var resumedSweeps = new List<Guid>();
        await RunStageAsync(dbContext, tasks.Where(t => t.Status == SweepTaskStatus.Fueling), batchSize, "打油",
            (task, taskCt) => DriveFuelingAsync(network, gateway, dbContext, task, GetHeadAsync, confirmations,
                fuelBnbLimit, gasBnbAlertThreshold, resumedSweeps, taskCt), ct);
        await RunStageAsync(dbContext, sweeping.Where(t => t.TxHash == null || resumedSweeps.Contains(t.Id)),
            batchSize, "待广播", DriveSweepAsync, ct);

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
        string stage, Func<ColSweepTask, CancellationToken, Task> processAsync, CancellationToken ct)
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
    private async Task TryActivateAsync(ColSweepTask candidate, decimal? roundLimit, CancellationToken ct)
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
                            || (!hasBroadcast && o.Status == DepositOrderStatus.Confirmed)))
            .OrderBy(o => o.CreationTime).ThenBy(o => o.Id)
            .ToListAsync(ct);
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
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505")
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
    }

    /// <summary>跟踪打油回执，发送前保存 nonce，使同一任务的重发只能替换原交易。</summary>
    private async Task DriveFuelingAsync(
        ColNetwork network, IBscChainGateway gateway, CollectionDbContext dbContext,
        ColSweepTask task, Func<CancellationToken, Task<long>> getHeadAsync, int confirmations,
        decimal fuelBnbLimit, decimal gasBnbAlertThreshold, List<Guid> resumedSweeps, CancellationToken ct)
    {
        var now = _clock.Now;

        // 已有打油在途：等回执，不重复广播（否则每轮 60s 连环打油烧 BNB）
        var gasNonce = task.GasNonceUsed;
        if (task.GasTxHash != null)
        {
            var gasReceipt = await gateway.GetTransactionReceiptAsync(task.GasTxHash, ct);
            if (gasReceipt.Status == null)
            {
                return; // 在途未确认：下一轮再看
            }

            if (gasReceipt.Status == 0)
            {
                if (gasReceipt.BlockNumber is long block
                    && await getHeadAsync(ct) - block >= confirmations)
                {
                    await dbContext.SweepTasks.Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling
                            && t.GasTxHash == task.GasTxHash && t.GasNonceUsed == gasNonce)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.GasTxHash, (string?)null)
                            .SetProperty(t => t.GasNonceUsed, (long?)null), ct);
                }
                return;
            }
        }

        var addressBnb = await gateway.GetBnbBalanceAsync(task.Address, ct);
        var gasPrice = await gateway.GetGasPriceGweiAsync(ct);
        var sweepGasCostBnb = gasPrice * 100_000m / 1_000_000_000m; // ERC20 transfer gas 上界
        if (addressBnb >= sweepGasCostBnb)
        {
            var rows = await dbContext.SweepTasks.Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, SweepTaskStatus.Sweeping)
                    .SetProperty(t => t.StatusChangedAt, now), ct);
            if (rows > 0 && task.TxHash != null)
            {
                resumedSweeps.Add(task.Id);
            }
            return;
        }

        // 需要打油：金额 = gas 成本 ×2（余量），超上限仅人工
        var fuelAmount = Math.Max(sweepGasCostBnb * 2m, 0.0005m);
        if (fuelAmount > fuelBnbLimit)
        {
            await FailAsync(dbContext, task.Id, SweepTaskStatus.Fueling, retryable: false,
                reason: $"打油金额 {fuelAmount:0.000000} BNB 超过上限 {fuelBnbLimit}（gas 价格异常？）", now);
            return;
        }

        // Gas 地址 BNB 余额检查（index 1）+ 过低告警（固定前缀 + 冷却）
        if (MasterKey == null)
        {
            await _alerts.RaiseAsync(null, "归集不可用",
                "Collection:MasterKey 未配置，无法解密助记词；归集轮空转。");
            return;
        }

        using var mnemonic = HdKeyService.Unprotect(network.MnemonicCipher!, MasterKey, _encryptor);
        var gasAddress = HdKeyService.DeriveAddress(mnemonic.Words, HdKeyService.GasAddressIndex);
        var gasBalance = await gateway.GetBnbBalanceAsync(gasAddress, ct);
        if (gasBalance < gasBnbAlertThreshold
            && ChainAlertService.ShouldAlert(network.GasLowAlertedAt, TimeSpan.FromMinutes(60), now))
        {
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixGasLow, "Gas 地址 BNB 过低",
                $"Gas 地址 {gasAddress} 余额 {gasBalance:0.000000} BNB 低于阈值 {gasBnbAlertThreshold}，归集打油将失败，请补充。");
            network.GasLowAlertedAt = now;
        }

        if (gasBalance < fuelAmount)
        {
            // Gas 不足：保持 Fueling 等运维补币（下轮再试），不置 Failed（可再试类）
            Logger.LogWarning("Gas 地址余额 {Balance} BNB 不足以打油 {Fuel}，任务 {TaskId} 等待补币",
                gasBalance, fuelAmount, task.Id);
            return;
        }

        var gasKey = HdKeyService.DerivePrivateKey(mnemonic.Words, HdKeyService.GasAddressIndex);
        var valueWei = "0x" + ToRawHex(fuelAmount, 18);
        if (task.GasTxHash != null)
        {
            return;
        }

        if (gasNonce == null)
        {
            // 同一个网络的打油账户由所有代币共用。存在未知发送结果时，先核对那一笔。
            var unresolved = await dbContext.SweepTasks.AnyAsync(t => t.NetworkId == task.NetworkId
                && t.Id != task.Id && t.Status == SweepTaskStatus.Fueling
                && t.GasNonceUsed != null && t.GasTxHash == null, ct);
            if (unresolved)
            {
                return;
            }

            gasNonce = await gateway.GetTransactionCountAsync(gasAddress, ct);
            using var gasUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var gasDb = await _dbContextProvider.GetDbContextAsync();
            var claimed = await gasDb.SweepTasks.Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling
                    && t.GasTxHash == null && t.GasNonceUsed == null)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.GasNonceUsed, gasNonce), ct);
            await gasUow.CompleteAsync(ct);
            if (claimed == 0)
            {
                return;
            }
        }

        var gasTx = await gateway.SendTransactionAsync(
            gasKey, task.Address, dataHex: "", valueWei, network.ChainId, nonce: gasNonce.Value, ct);
        await dbContext.SweepTasks
            .Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling
                && t.GasNonceUsed == gasNonce && t.GasTxHash == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.GasTxHash, gasTx), ct);
        Logger.LogInformation("归集任务 {TaskId} 已提交 {Fuel} BNB 打油交易 {Hash}，等待回执", task.Id, fuelAmount, gasTx);
    }

    /// <summary>未知候选仅阻止重签。任一通过确认与转账核对的候选可完成原任务。</summary>
    private async Task DriveSweepingAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway, CollectionDbContext dbContext,
        ColSweepTask task, Func<CancellationToken, Task<long>> getHeadAsync, int confirmations,
        TimeSpan rebroadcastGrace, CancellationToken ct)
    {
        var now = _clock.Now;
        var head = task.TxHash == null ? 0 : await getHeadAsync(ct);
        var (receipt, uncertain, transferMismatch) = await FindSweepReceiptAsync(
            gateway, task, token, head, confirmations, ct);
        if (receipt == null)
        {
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
                    await dbContext.DepositOrders
                        .Where(o => promotable.Contains(o.Id) && o.Status == DepositOrderStatus.Confirmed)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(o => o.Status, DepositOrderStatus.Sweeping), ct);
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

            if (MasterKey == null)
            {
                await _alerts.RaiseAsync(null, "归集不可用", "Collection:MasterKey 未配置，无法签名归集交易。");
                return;
            }

            using var mnemonic = HdKeyService.Unprotect(network.MnemonicCipher!, MasterKey, _encryptor);
            var userKey = HdKeyService.DerivePrivateKey(mnemonic.Words, task.AddressIndex);
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
            var nonce = task.NonceUsed ?? await gateway.GetTransactionCountAsync(task.Address, ct);
            var transaction = await gateway.PrepareTransactionAsync(
                userKey, token.ContractAddress, dataHex, "0x0", network.ChainId, nonce, ct);
            // 签名可能与上次相同。只保留不同的候选哈希，覆盖节点已收单但发送响应丢失的情况。
            var previousHashes = SweepHashes(task)
                .Where(hash => !string.Equals(hash, transaction.Hash, StringComparison.OrdinalIgnoreCase))
                .ToArray();
            var claimedAt = _clock.Now;
            int claimed;
            using (var broadcastUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
            {
                var broadcastDb = await _dbContextProvider.GetDbContextAsync();
                claimed = await MatchSweepAttempt(broadcastDb, task)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.TxHash, transaction.Hash)
                        .SetProperty(t => t.PreviousTxHashes, previousHashes.Length == 0 ? null : previousHashes)
                        .SetProperty(t => t.BroadcastAt, claimedAt)
                        .SetProperty(t => t.BroadcastVaultAddress, vaultAddress)
                        .SetProperty(t => t.NonceUsed, nonce), ct);
                await broadcastUow.CompleteAsync(ct);
            }
            if (claimed == 0)
            {
                return;
            }

            await gateway.BroadcastTransactionAsync(transaction, ct);
            Logger.LogInformation("任务 {TaskId} 归集已广播 {Amount} {Symbol}（{Hash}）",
                task.Id, task.Amount, token.Symbol, transaction.Hash);
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
                .SetProperty(t => t.TxBlockNumber, (long?)null), ct);
            return;
        }

        // 回执在块里：确认数不够 → 保持 Sweeping 和原哈希，下轮再看（不增加替换档、不重签）
        await MatchSweepAttempt(dbContext, task)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.TxBlockNumber, receiptBlock), ct);
        if (head - receiptBlock < confirmations)
        {
            return;
        }

        // 确认总转账与关联单据毛额一致后，任务、余额、凭证和单据状态才一同提交。
        using (var creditUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var creditDbContext = await _dbContextProvider.GetDbContextAsync();
            var linked = await creditDbContext.DepositOrders
                .Where(o => o.SweepTaskId == task.Id && o.Status == DepositOrderStatus.Sweeping)
                .ToListAsync(ct);
            var linkedGross = linked.Sum(order => order.Amount ?? 0m);
            if (linked.Count == 0 || linkedGross != task.Amount)
            {
                await FailSweepAttemptAsync(creditDbContext, task,
                    $"链接单毛额合计 {linkedGross} 与广播金额 {task.Amount} 不一致，拒绝入账待人工核对", now, ct);
                await creditUow.CompleteAsync(ct);
                await _alerts.RaiseAsync(null, "归集卡住",
                    $"任务 {task.Id} 链上确认但链接单毛额合计 {linkedGross} ≠ 广播金额 {task.Amount}，拒绝自动入账");
                return;
            }

            var rows = await MatchSweepAttempt(creditDbContext, task)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, SweepTaskStatus.Swept)
                    .SetProperty(t => t.TxHash, receipt.TxHash)
                    .SetProperty(t => t.PreviousTxHashes, (string[]?)null)
                    .SetProperty(t => t.TxBlockNumber, receiptBlock)
                    .SetProperty(t => t.StatusChangedAt, now), ct);
            if (rows == 0)
            {
                return;
            }

            foreach (var order in linked)
            {
                // 用户余额记净额。账本分别记毛额和手续费，用户负债的净变动与余额一致。
                var gross = order.Amount ?? 0m;
                var credit = CollectionLedgerPoster.ResolveCreditAmount(
                    order.Amount, order.ActualAmount, order.Fee);
                var currency = MapCurrency(token.Symbol);
                await _balanceManager.CreditAsync(
                    order.UserId, currency, credit,
                    idempotencyKey: BalanceIdempotencyKeys.ForDepositOrder(order.Id));
                await _ledgerPoster.PostDepositCreditAsync(order.Id, order.UserId, currency, gross);
                await _ledgerPoster.PostDepositFeeAsync(order.Id, order.UserId, currency, order.Fee ?? 0m);
                order.Status = DepositOrderStatus.Swept;
            }

            // 解绑旧批次里残留的 Confirmed 单据，供下轮重新挂队。
            await creditDbContext.DepositOrders
                .Where(o => o.SweepTaskId == task.Id && o.Status == DepositOrderStatus.Confirmed)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.SweepTaskId, (Guid?)null), ct);

            await creditUow.CompleteAsync();
            Logger.LogInformation("归集任务 {TaskId} 确认毛额 {GrossAmount} {Symbol}，已结算 {Count} 笔进款",
                task.Id, task.Amount, token.Symbol, linked.Count);
        }
    }

    /// <summary>坏回执只影响对应哈希，旧候选继续核对。缺少可靠查单结果时保持原 nonce 和候选。</summary>
    private async Task<(TransactionReceipt? Receipt, bool Uncertain, bool TransferMismatch)> FindSweepReceiptAsync(
        IBscChainGateway gateway, ColSweepTask task, ColToken token, long head, int confirmations, CancellationToken ct)
    {
        TransactionReceipt? unconfirmed = null;
        var uncertain = false;
        var transferMismatch = false;
        foreach (var hash in SweepHashes(task))
        {
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
        var raw = scaled.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

    private static CollectionCurrency MapCurrency(string symbol)
    {
        // fail-closed：未知符号直接抛，绝不当成 USDT 入账（未来代币可配置时的护栏）
        if (symbol.Equals("USDC", StringComparison.OrdinalIgnoreCase)) return CollectionCurrency.Usdc;
        if (symbol.Equals("USDT", StringComparison.OrdinalIgnoreCase)) return CollectionCurrency.Usdt;
        throw new InvalidOperationException("未知稳定币符号，拒绝入账：" + symbol);
    }
}
