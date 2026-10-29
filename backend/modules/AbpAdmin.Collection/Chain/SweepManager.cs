using AbpAdmin.Collection.Ledger;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
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
/// 归集一轮（计划第 5.6 节）。写入纪律：列表全部 AsNoTracking，全部写入走带状态守卫的
/// ExecuteUpdate——跟踪实体与原子更新混用会让轮末 SaveChanges 用陈旧值覆盖回列（实测抓到：
/// 冷却拉回的任务停在 Queued、TxHash 丢失）。跨 worker 的互斥由部分唯一索引兜底。
/// 同址同币合并、每轮取出上限、同址两币不同时广播；冷却看 StatusChangedAt。
/// 金库地址取自 ColToken；金库私钥系统不保存；用户地址私钥按助记词现推、不落日志。
/// </summary>
/// <summary>
/// 归集一轮（计划第 5.6 节）：可再试失败冷却拉回 → Queued 激活（同址同币合并、每轮取出上限、
/// 地址级抢占——同址已有 Fueling/Sweeping 时另一币不能再占，部分唯一索引兜底）→ 打油/广播 →
/// 确认数达标同事务标 Swept + 按进款单逐笔入账（IdempotencyKey = 进款单 id）+ 提交后发
/// BalanceCreditedEto。确认数不够保持 Sweeping 和原哈希不重签；宽限后无回执才重签（同 nonce 替换）。
/// 金库地址取自 ColToken；金库私钥系统不保存；用户地址私钥按助记词现推、不落日志。
/// </summary>
public class SweepManager : DomainService
{
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

    /// <summary>主密钥由 worker 装配注入（Collection:MasterKey；null = 归集不可用，轮次空转并告警）。</summary>
    public SweepManager WithMasterKey(string? masterKey)
    {
        _masterKey = masterKey;
        return this;
    }

    public virtual async Task RunTokenRoundAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway,
        int confirmations, decimal? sweepRoundLimit, decimal fuelBnbLimit,
        TimeSpan retryCooldown, TimeSpan rebroadcastGrace, TimeSpan stuckThreshold,
        decimal gasBnbAlertThreshold,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token.VaultAddress))
        {
            Logger.LogWarning("代币 {Symbol} 未配置金库地址，归集轮空转（Confirmed 单保持待归集）", token.Symbol);
            return;
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;

        // 分离实体防丢：心跳与告警冷却列必须落在本工作单元的受追踪行上
        network = await dbContext.Networks.SingleAsync(n => n.Id == network.Id, ct);
        network.SweepHeartbeatAt = now;

        // ① 可再试失败：冷却（StatusChangedAt）后拉回 Queued
        await dbContext.SweepTasks
            .Where(t => t.TokenId == token.Id && t.Status == SweepTaskStatus.Failed && t.Retryable
                        && now - t.StatusChangedAt >= retryCooldown)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, SweepTaskStatus.Queued)
                .SetProperty(t => t.Retryable, false)
                .SetProperty(t => t.FailReason, (string?)null)
                .SetProperty(t => t.StatusChangedAt, now), ct);

        // ② 激活 Queued（同址同币合并 + 上限 + 地址级抢占）
        var queued = await dbContext.SweepTasks.AsNoTracking()
            .Where(t => t.TokenId == token.Id && t.Status == SweepTaskStatus.Queued)
            .OrderBy(t => t.CreationTime)
            .ToListAsync(ct);
        foreach (var task in queued)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await TryActivateAsync(dbContext, task, sweepRoundLimit, ct);
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pg && pg.SqlState == "23505")
            {
                // 地址级互斥的部分唯一索引兜底：并发 worker 先占了同地址（另一币）→ 本轮放弃该任务，
                // 不让单任务冲突掀翻整轮（与「同址两币不同时广播」同一规则的并发形态）
                Logger.LogWarning("任务 {TaskId} 激活撞地址级互斥索引（并发抢占），本轮跳过", task.Id);
            }
        }

        // ③ Fueling：地址有 gas → Sweeping；没有 → 打油（超上限仅人工；Gas 地址见底告警）
        var fueling = await dbContext.SweepTasks.AsNoTracking()
            .Where(t => t.TokenId == token.Id && t.Status == SweepTaskStatus.Fueling)
            .ToListAsync(ct);
        foreach (var task in fueling)
        {
            ct.ThrowIfCancellationRequested();
            await DriveFuelingAsync(network, token, gateway, dbContext, task, fuelBnbLimit, gasBnbAlertThreshold, ct);
        }

        // ④ Sweeping：广播 → 回执 → 确认数 → Swept + 入账
        var sweeping = await dbContext.SweepTasks.AsNoTracking()
            .Where(t => t.TokenId == token.Id && t.Status == SweepTaskStatus.Sweeping)
            .ToListAsync(ct);
        foreach (var task in sweeping)
        {
            ct.ThrowIfCancellationRequested();
            await DriveSweepingAsync(network, token, gateway, dbContext, task, confirmations, rebroadcastGrace, ct);
        }

        // ⑤ 归集卡住告警（Sweeping 停留超过阈值；固定前缀）
        var stuck = sweeping.FirstOrDefault(t => now - t.StatusChangedAt > stuckThreshold);
        if (stuck != null && ChainAlertService.ShouldAlert(network.SweepStuckAlertedAt, TimeSpan.FromMinutes(60), now))
        {
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixSweepStuck, "归集卡住",
                $"代币 {token.Symbol} 任务 {stuck.Id}（地址 {stuck.Address}）在 Sweeping 停留超过 {stuckThreshold.TotalMinutes:0} 分钟（TxHash={stuck.TxHash ?? "未广播"}）。");
            network.SweepStuckAlertedAt = now;
        }

        await dbContext.SaveChangesAsync(ct);
        await uow.CompleteAsync();
    }

    /// <summary>激活：链接进款单（合并 + 上限）→ 按 gas 决定 Fueling / Sweeping；地址被占则本轮放弃。</summary>
    private async Task TryActivateAsync(
        CollectionDbContext dbContext, ColSweepTask task, decimal? roundLimit, CancellationToken ct)
    {
        // 地址级抢占：同地址已有另一币的 Fueling/Sweeping → 本轮不动（部分唯一索引兜底并发）
        var addressBusy = await dbContext.SweepTasks
            .AnyAsync(t => t.AddressId == task.AddressId
                           && t.TokenId != task.TokenId
                           && (t.Status == SweepTaskStatus.Fueling || t.Status == SweepTaskStatus.Sweeping), ct);
        if (addressBusy)
        {
            return;
        }

        // Confirmed = 首次激活；Sweeping = 可再试失败拉回后重驱（单已链接，状态停在广播中）
        // 可链接单：首次 Confirmed + 重试时停在 Sweeping 的已链接单
        var linked = await dbContext.DepositOrders.AsNoTracking()
            .Where(o => o.SweepTaskId == task.Id
                        && (o.Status == DepositOrderStatus.Confirmed || o.Status == DepositOrderStatus.Sweeping))
            .OrderBy(o => o.CreationTime)
            .ToListAsync(ct);
        if (linked.Count == 0)
        {
            return;
        }

        // 每轮取出上限：装到装不下为止（至少一笔，避免死锁在极小限额上）
        decimal sum = 0m;
        var pickedIds = new List<Guid>();
        for (var i = 0; i < linked.Count; i++)
        {
            var value = linked[i].Amount ?? 0m;
            if (i > 0 && roundLimit.HasValue && sum + value > roundLimit.Value)
            {
                break;
            }

            pickedIds.Add(linked[i].Id);
            sum += value;
        }

        var now = _clock.Now;

        // 先条件占位任务（WHERE Status='Queued'）；落空 = 已被并发激活。
        // 重签宽限以 BroadcastAt 计，激活重置 StatusChangedAt 不影响它。
        // 地址级互斥索引（AddressId 上 Status IN (1,2)）会把「同地址已有 Fueling/Sweeping
        // 僵尸任务」的激活打成 23505——是「地址被占」的正常语义而非故障：捕获后本轮跳过该
        // 任务（卡死告警段会盯住僵尸），此前未捕获会炸掉整轮归集（E2E 实测：Sweeping 僵尸
        // 占位 + 8433 进款单堆积，激活每轮必撞、归集全线停摆）
        // NV-18：claim/翻状态/解链三条语句合并进一个内层事务。此前是三条独立自动提交，
        // 中断窗口（CollectionChainWorkerBase 的轮预算取消是真中断源）会留下「任务已 Fueling
        // 且 Amount 已按选中子集聚合、尾巴单仍链接+Confirmed」的部分激活态；下一轮自愈无条件
        // 升级全部链接 Confirmed 单并在确认时全额入账，而链上只广播了选中子集金额 ⇒ 超限入账。
        // 原子化后要么三句全落、要么任务留 Queued 下轮重来。三条语句在绑定内层事务的
        // DbContext 上执行（外层轮 UoW 有意非事务，见 RunTokenRoundAsync 注释）。
        using var activateUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var txDb = await _dbContextProvider.GetDbContextAsync();
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
            // ExecuteUpdateAsync 把约束冲突的 PostgresException 直接抛出（不包 DbUpdateException——
            // E2E 实测穿透了只捕 DbUpdateException 的第一版谓词）；内层事务随 dispose 回滚
            Logger.LogWarning(
                "任务 {TaskId} 激活被地址互斥拦下（同地址存在 Fueling/Sweeping 任务），本轮跳过；僵尸由卡死告警段处置",
                task.Id);
            return;
        }
        if (claimed == 0)
        {
            return;
        }

        // 选中单标记 Sweeping（只认 Confirmed/Sweeping，原子条件防串）
        await txDb.DepositOrders
            .Where(o => pickedIds.Contains(o.Id)
                        && (o.Status == DepositOrderStatus.Confirmed || o.Status == DepositOrderStatus.Sweeping))
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, DepositOrderStatus.Sweeping), ct);

        // 超上限的尾巴单解链（SweepTaskId=null）：广播自愈只翻「本任务已链接的 Confirmed 单」，
        // 不解链它们会被自愈误升级为 Sweeping 并在确认时入账——但链上只转了选中子集的金额
        //（评审 C 级：RoundLimit 被自愈击穿）。解链后由扫链轮确认推进重新链接到下一个任务
        await txDb.DepositOrders
            .Where(o => o.SweepTaskId == task.Id && o.Status == DepositOrderStatus.Confirmed
                        && !pickedIds.Contains(o.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.SweepTaskId, (Guid?)null), ct);

        await activateUow.CompleteAsync(ct);
    }

    private async Task DriveFuelingAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway, CollectionDbContext dbContext,
        ColSweepTask task, decimal fuelBnbLimit, decimal gasBnbAlertThreshold, CancellationToken ct)
    {
        var now = _clock.Now;

        // 已有打油在途：等回执，不重复广播（否则每轮 60s 连环打油烧 BNB，评审 M 级）
        if (task.GasTxHash != null)
        {
            var gasReceipt = await gateway.GetTransactionReceiptAsync(task.GasTxHash, ct);
            if (gasReceipt.Status == null)
            {
                return; // 在途未确认：下一轮再看
            }

            if (gasReceipt.Status == 0)
            {
                // 打油交易失败：清掉在途标记，本轮重新评估
                await dbContext.SweepTasks
                    .Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.GasTxHash, (string?)null), ct);
            }
        }

        var addressBnb = await gateway.GetBnbBalanceAsync(task.Address, ct);
        var gasPrice = await gateway.GetGasPriceGweiAsync(ct);
        var sweepGasCostBnb = gasPrice * 100_000m / 1_000_000_000m; // ERC20 transfer gas 上界
        if (addressBnb >= sweepGasCostBnb)
        {
            await TransitionAsync(dbContext, task, SweepTaskStatus.Fueling, SweepTaskStatus.Sweeping, now);
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
        var gasTx = await gateway.SendTransactionAsync(
            gasKey, task.Address, dataHex: "", valueWei, network.ChainId, nonce: await gateway.GetTransactionCountAsync(gasAddress, ct), ct);
        await dbContext.SweepTasks
            .Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Fueling)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.GasTxHash, gasTx), ct);
        // 保持 Fueling：下一轮看到地址有 gas 自然推进 Sweeping
        Logger.LogInformation("任务 {TaskId} 已打油 {Fuel} BNB（{Hash}）", task.Id, fuelAmount, gasTx);
    }

    private async Task DriveSweepingAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway, CollectionDbContext dbContext,
        ColSweepTask task, int confirmations, TimeSpan rebroadcastGrace, CancellationToken ct)
    {
        var now = _clock.Now;
        var head = await gateway.GetLatestBlockNumberAsync(ct);

        // 未广播：先自愈（激活的 claim 与选中单翻状态是两条自动提交语句，崩溃窗口会留下
        // 已选中但仍是 Confirmed 的链接单），再签名广播（用户地址私钥现推）。
        // 超上限尾巴单已在激活时解链，不会被这里误升级（评审 C 级）
        if (task.TxHash == null)
        {
            // NV-18 第二道：自愈升级加金额上限——只升到 task.Amount（= 链上将广播的金额）装得下
            // 为止。激活原子化后新数据不会再出现「尾巴单仍链接」的中间态，但存量库的历史部分
            // 激活行/人工补链仍可能让链接 Confirmed 合计超过广播金额；无条件升级会在确认时
            // 全额入账（金库缺口）。超额者留在 Confirmed 由对账/人工路径处置。
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
            if (linkable.Count == 0)
            {
                // 链接单全部丢失（人工清理过？）：不广播真金，任务置失败仅人工
                await FailAsync(dbContext, task.Id, SweepTaskStatus.Sweeping, retryable: false,
                    reason: "任务下没有任何 Sweeping 链接进款单，拒绝盲目归集", now);
                return;
            }

            if (MasterKey == null)
            {
                await _alerts.RaiseAsync(null, "归集不可用", "Collection:MasterKey 未配置，无法签名归集交易。");
                return;
            }

            using var mnemonic = HdKeyService.Unprotect(network.MnemonicCipher!, MasterKey, _encryptor);
            var userKey = HdKeyService.DerivePrivateKey(mnemonic.Words, task.AddressIndex);
            string dataHex;
            try
            {
                dataHex = BuildErc20TransferData(token.VaultAddress!, task.Amount, token.Decimals);
            }
            catch (OverflowException)
            {
                // 合计金额超出 decimal 可换算范围（10 位整数上限的单不该聚到一起）：仅人工
                await FailAsync(dbContext, task.Id, SweepTaskStatus.Sweeping, retryable: false,
                    reason: $"任务金额 {task.Amount} 超出可换算范围", now);
                return;
            }
            var nonce = task.NonceUsed ?? await gateway.GetTransactionCountAsync(task.Address, ct);
            var txHash = await gateway.SendTransactionAsync(
                userKey, token.VaultAddress!, dataHex, "0x0", network.ChainId, nonce, ct);

            await dbContext.SweepTasks
                .Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Sweeping)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.TxHash, txHash)
                    .SetProperty(t => t.BroadcastAt, now)
                    .SetProperty(t => t.NonceUsed, nonce), ct); // StatusChangedAt 不动：宽限/卡住以 BroadcastAt 计
            Logger.LogInformation("任务 {TaskId} 归集已广播 {Amount} {Symbol}（{Hash}）",
                task.Id, task.Amount, token.Symbol, txHash);
            return;
        }

        // 已广播：查回执
        var receipt = await gateway.GetTransactionReceiptAsync(task.TxHash, ct);
        if (receipt.Status == null)
        {
            // 无回执：宽限（从 BroadcastAt 起算）之内保持原哈希不重签；超时才允许同 nonce 替换
            if (now - (task.BroadcastAt ?? now) > rebroadcastGrace)
            {
                Logger.LogWarning("任务 {TaskId} 广播超宽限无回执，按同 nonce {Nonce} 重签替换", task.Id, task.NonceUsed);
                await dbContext.SweepTasks
                    .Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Sweeping)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(t => t.TxHash, (string?)null)
                        .SetProperty(t => t.BroadcastAt, (DateTime?)null), ct); // NonceUsed 保留：替换必须复用
            }

            return;
        }

        if (receipt.Status == 0)
        {
            // 链上 revert：归集转出失败多为代币暂停/黑名单——固定前缀告警 + 仅人工
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixTokenPaused, "代币暂停或转账被拒",
                $"代币 {token.Symbol} 归集交易 {task.TxHash} 链上失败（revert），请人工核查合约状态。");
            await FailAsync(dbContext, task.Id, SweepTaskStatus.Sweeping, retryable: false, reason: "归集交易链上 revert", now);
            return;
        }

        // 回执在块里：确认数不够 → 保持 Sweeping 和原哈希，下轮再看（不增加替换档、不重签）
        await dbContext.SweepTasks
            .Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Sweeping)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.TxBlockNumber, receipt.BlockNumber), ct);
        if (head - receipt.BlockNumber!.Value < confirmations)
        {
            return;
        }

        // NV-18 第三道（资金时刻的对账门）：入账前先核对链接单毛额合计 == task.Amount（= 链上
        // 广播金额）。不等 ⇒ 说明链接集合与广播子集漂移（历史部分激活/人工补链），此时逐笔
        // 入账就是把没上链的钱记给用户（金库缺口）。fail-closed：任务置失败仅人工，单保持
        // Sweeping 不入账，交卡单告警 + 对账处置。
        var pendingCredit = await dbContext.DepositOrders.AsNoTracking()
            .Where(o => o.SweepTaskId == task.Id && o.Status == DepositOrderStatus.Sweeping)
            .Select(o => new { o.Id, Amount = (decimal?)o.Amount ?? 0m })
            .ToListAsync(ct);
        var pendingGross = pendingCredit.Sum(x => x.Amount);
        if (pendingGross != task.Amount)
        {
            await _alerts.RaiseAsync(null, "归集卡住",
                $"任务 {task.Id} 链上确认但链接单毛额合计 {pendingGross} ≠ 广播金额 {task.Amount}，拒绝自动入账");
            await FailAsync(dbContext, task.Id, SweepTaskStatus.Sweeping, retryable: false,
                reason: $"链接单毛额合计 {pendingGross} 与广播金额 {task.Amount} 不一致，拒绝入账待人工核对", now);
            return;
        }

        // 确认达标：同一事务里标 Swept + 按进款单逐笔入账（CreditAsync 加入本工作单元）
        using (var creditUow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var creditDbContext = await _dbContextProvider.GetDbContextAsync();
            var linked = await creditDbContext.DepositOrders
                .Where(o => o.SweepTaskId == task.Id && o.Status == DepositOrderStatus.Sweeping)
                .ToListAsync(ct);
            foreach (var order in linked)
            {
                // 入账净额（线上口径：充值入账的是建单快照净额；历史单无快照回退毛额）。
                // BalanceCreditedEto 在 CreditAsync 内携带同一净额，只发一次。
                // 两本一致收敛到净额：余额行直接入净额（BalanceCreditedEto 同净额，只发一次）；
                // 账本按线上口径过毛额（借 1001 / 贷 2201）再扣手续费（借 2201 / 贷 6003），
                // 2201 净额 = 毛额 − fee = 余额行。若两侧都用净额+fee 再扣会把用户扣两遍（实测抓出）。
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

            if (linked.Count == 0)
            {
                // 链上已转出但没有任何链接进款单：绝不能标 Swept（资金已动、用户未入账）。
                // 保持 Sweeping 让「归集卡住」告警接手，人工核对链接关系
                Logger.LogError(CollectionConsts.AlertPrefixSweepStuck + "任务 {TaskId} 链上确认但链接进款单为 0，保持 Sweeping 等人工", task.Id);
                await creditUow.CompleteAsync();
                return;
            }

            var rows = await creditDbContext.SweepTasks
                .Where(t => t.Id == task.Id && t.Status == SweepTaskStatus.Sweeping)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, SweepTaskStatus.Swept)
                    .SetProperty(t => t.StatusChangedAt, now), ct);
            if (rows == 0)
            {
                throw new InvalidOperationException("归集任务确认状态被并发改变：" + task.Id);
            }

            // 防御性释放：正常流尾巴单已在激活时解链；这里兜底清理残留的 Confirmed 链接单
            await creditDbContext.DepositOrders
                .Where(o => o.SweepTaskId == task.Id && o.Status == DepositOrderStatus.Confirmed)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.SweepTaskId, (Guid?)null), ct);

            await creditUow.CompleteAsync();
            Logger.LogInformation("任务 {TaskId} 归集确认并入账 {Amount} {Symbol}（{Count} 笔进款）",
                task.Id, task.Amount, token.Symbol, linked.Count);
        }
    }

    private static Task TransitionAsync(CollectionDbContext dbContext, ColSweepTask task,
        SweepTaskStatus from, SweepTaskStatus next, DateTime now)
    {
        return dbContext.SweepTasks
            .Where(t => t.Id == task.Id && t.Status == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, next)
                .SetProperty(t => t.StatusChangedAt, now));
    }

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
