using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Permissions;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Collection.Withdrawals;
using AbpAdmin.Collection.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

/// <summary>审核/拒绝入参（拒绝原因必填）。</summary>
public class WithdrawAuditInput
{
    public Guid Id { get; set; }

    /// <summary>拒绝必填；审批可留空的备注。</summary>
    public string? Remark { get; set; }
}

/// <summary>配置提现热钱包（私钥经 body 传递，不进 URL/访问日志）。</summary>
public class SetWithdrawVaultInput
{
    public string Address { get; set; } = default!;

    public string PrivateKey { get; set; } = default!;
}

/// <summary>热钱包监控流水行（金额超精度时 Amount 为 null，展示回退 RawAmount）。</summary>
public class HotWalletTxnDto
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = default!;
    public string TxHash { get; set; } = default!;
    public int LogIndex { get; set; }
    public string From { get; set; } = default!;
    public string To { get; set; } = default!;

    /// <summary>Out / In。</summary>
    public string Direction { get; set; } = default!;

    /// <summary>缩放后金额（十进制字符串）；null = 超精度。</summary>
    public string? Amount { get; set; }

    public string RawAmount { get; set; } = default!;
    public long BlockNumber { get; set; }

    /// <summary>Matched / ExternalIn / Anomaly / Acked。</summary>
    public string Status { get; set; } = default!;

    /// <summary>出站匹配到的提现单 Id。</summary>
    public Guid? MatchedWithdrawId { get; set; }

    public string? AckedBy { get; set; }
    public DateTime? AckedAt { get; set; }
    public string? AckNote { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>热钱包监控状态卡（游标/异常计数；余额熔断走 GetHotWallet）。</summary>
public class HotWalletWatchStatusDto
{
    /// <summary>未处置异常出账数（红点口径）。</summary>
    public int AnomalyOpenCount { get; set; }

    public List<HotWalletWatchTokenDto> Tokens { get; set; } = new();
}

/// <summary>单币监控通道状态。</summary>
public class HotWalletWatchTokenDto
{
    public string Symbol { get; set; } = default!;

    /// <summary>已扫到的安全链头；null = 尚未首跑。</summary>
    public long? LastScannedBlock { get; set; }

    public DateTime? LastScanAt { get; set; }

    /// <summary>最近一轮失败原因（成功后清空）。</summary>
    public string? LastError { get; set; }
}

/// <summary>
/// 提现管理端（审核/拒绝/列表/状态）。广播重试、人工完结、热钱包状态在 M3 扩展。
/// Host 专用权限（WithdrawAdmin）+ AssertHost 双保险（同 Network.Admin 纪律）。
/// </summary>
[Authorize(CollectionPermissions.Withdraw.Admin)]
public class CollectionWithdrawAdminAppService : ApplicationService
{
    private readonly WithdrawManager _withdraws;
    private readonly CollectionLedgerPoster _ledger;
    private readonly WithdrawGuard _guard;
    private readonly WithdrawBroadcastManager _broadcast;
    private readonly AbpAdmin.Collection.Chain.ChainGatewayFactory _gatewayFactory;
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ISettingProvider _settings;
    private readonly Volo.Abp.SettingManagement.ISettingManager _settingManager;

    public CollectionWithdrawAdminAppService(
        WithdrawManager withdraws,
        CollectionLedgerPoster ledger,
        WithdrawGuard guard,
        WithdrawBroadcastManager broadcast,
        AbpAdmin.Collection.Chain.ChainGatewayFactory gatewayFactory,
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ISettingProvider settings,
        Volo.Abp.SettingManagement.ISettingManager settingManager)
    {
        _withdraws = withdraws;
        _ledger = ledger;
        _guard = guard;
        _broadcast = broadcast;
        _gatewayFactory = gatewayFactory;
        _dbContextProvider = dbContextProvider;
        _settings = settings;
        _settingManager = settingManager;
    }

    /// <summary>提现单列表（可按状态/用户过滤，倒序分页）。</summary>
    public virtual async Task<PagedResultDto<WithdrawOrderDto>> GetWithdrawalsAsync(
        int skipCount, int maxResultCount, string? status = null, Guid? userId = null)
    {
        AssertHost();
        if (maxResultCount <= 0 || maxResultCount > 200)
        {
            maxResultCount = 20;
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var query = dbContext.WithdrawOrders.AsNoTracking();
        if (!string.IsNullOrEmpty(status)
            && Enum.TryParse<AbpAdmin.Collection.Entities.WithdrawStatus>(status, true, out var st))
        {
            query = query.Where(o => o.Status == st);
        }

        if (userId.HasValue)
        {
            query = query.Where(o => o.UserId == userId.Value);
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(o => o.CreationTime)
            .Skip(Math.Max(0, skipCount))
            .Take(maxResultCount)
            .ToListAsync();

        return new PagedResultDto<WithdrawOrderDto>(
            total, rows.Select(WithdrawOrderDto.From).ToList());
    }

    /// <summary>
    /// 审核通过（Pending → Processing：冻结出账、净额转应付、手续费转收入）。
    /// 审批前过护栏：热钱包偿付 + 熔断地板 + BNB 门槛（worker 小额自动审同款检查；
    /// 广播前 worker 还有一道，签名临界区内最后一道——三道检查对齐 exchange）。
    /// </summary>
    public virtual async Task ApproveAsync(WithdrawAuditInput input)
    {
        AssertHost();
        await EnsureSolvencyAsync(input.Id);
        await _withdraws.ApproveAsync(input.Id, CurrentUser.Id, input.Remark);
    }

    /// <summary>
    /// 提现运维端点统一的网关装配（原先四处逐份拷贝、缺网络行的错误码还各不相同）。
    /// cap 沿用扫链的 NodeLogResultCap 但允许缺省 10000：提现路径只调 eth_*（nonce/余额/签名/回执），
    /// 不消费 getLogs 截断启发式——「无实测上限拒绝工作」是扫链侧的规则，此处豁免且不伪造测量语义。
    /// </summary>
    private async Task<(Entities.ColNetwork Network, Chain.IBscChainGateway Gateway)> CreateWithdrawGatewayAsync()
    {
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks.AsNoTracking()
            .OrderBy(n => n.CreationTime).FirstAsync()
            ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        var gateway = await _gatewayFactory.CreateAsync(dbContext, network, network.NodeLogResultCap ?? 10000);
        await uow.CompleteAsync();
        return (network, gateway);
    }

    /// <summary>热钱包偿付护栏（审批端点共用；RPC 不可用时失败拒绝——fail-closed）。</summary>
    private async Task EnsureSolvencyAsync(Guid orderId)
    {
        var order = await _withdraws.FindAsync(orderId);
        var wallet = await _guard.LoadAsync()
                     ?? throw new BusinessException(CollectionErrorCodes.HotWalletNotConfigured);
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var gateway = await _gatewayFactory.CreateAsync(dbContext, wallet.Network, wallet.Network.NodeLogResultCap ?? 10000);
        var symbol = CollectionLedgerPoster.CurrencyCode(order.Currency);
        var token = await dbContext.Tokens.AsNoTracking()
            .FirstAsync(t => t.NetworkId == wallet.Network.Id && t.Symbol == symbol);
        await _guard.EnsureCanPayAsync(wallet, gateway, token, order.ActualAmount);
    }

    /// <summary>拒绝（Pending → Rejected：解冻、释放额度；原因必填）。</summary>
    public virtual async Task RejectAsync(WithdrawAuditInput input)
    {
        AssertHost();
        await _withdraws.RejectAsync(input.Id, CurrentUser.Id!.Value, input.Remark!);
    }

    /// <summary>
    /// 广播失败退款终态（BroadcastFailed → Rejected：净额+手续费退回用户可用；原因必填）。
    /// 链上双花闸（不再仅凭操作员判断）：带哈希的单必须回执明确失败（revert=可证未出款；
    /// 成功 → 引导走人工完结，回执未定 → 拒绝）；无哈希的单必须 pending nonce 未越过
    /// NonceUsed（越过 = 可能已上链）。验证不过一律拒绝——宁可多一步人工也不双付。
    /// </summary>
    public virtual async Task RefundAsync(WithdrawAuditInput input)
    {
        AssertHost();
        var order = await _withdraws.FindAsync(input.Id);
        var (network, gateway) = await CreateWithdrawGatewayAsync();

        if (!string.IsNullOrEmpty(order.TxHash))
        {
            var receipt = await gateway.GetTransactionReceiptAsync(order.TxHash);
            if (receipt.Status == null)
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawPendingOutbound)
                    .WithData("TxHash", order.TxHash);
            }

            if (receipt.Status == 1)
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus)
                    .WithData("Reason", "链上回执成功——该单已出款，请走人工完结而非退款");
            }
        }
        else if (order.NonceUsed != null)
        {
            var wallet = await _guard.LoadAsync()
                         ?? throw new BusinessException(CollectionErrorCodes.HotWalletNotConfigured);
            var pending = await gateway.GetTransactionCountAsync(wallet.Address);
            if (pending > order.NonceUsed.Value)
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawPendingOutbound)
                    .WithData("Reason", $"pending nonce {pending} 已越过 NonceUsed {order.NonceUsed}，交易可能在途");
            }
        }

        await _withdraws.RefundAfterApproveAsync(input.Id, CurrentUser.Id!.Value, input.Remark!);
    }

    /// <summary>提现配置快照（前端状态卡：开关/总闸/限额/费率 + 各状态单量）。</summary>
    public virtual async Task<WithdrawStatusDto> GetStatusAsync()
    {
        AssertHost();
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var counts = await dbContext.WithdrawOrders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync();
        var byStatus = counts.ToDictionary(x => x.Key, x => x.Count);

        return new WithdrawStatusDto
        {
            Enabled = await _withdraws.IsEnabledAsync(),
            Halt = await _settings.IsTrueAsync(CollectionSettings.Withdraw.Halt),
            // 展示兜底走 WithdrawDefaults 单源（原先三处手抄字符串会各自漂移）
            MinAmount = await _settings.GetOrNullAsync(CollectionSettings.Withdraw.MinAmount)
                ?? WithdrawDefaults.MinAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            FeeRate = await _settings.GetOrNullAsync(CollectionSettings.Withdraw.FeeRate)
                ?? WithdrawDefaults.FeeRate.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MaxDailyAmount = await _settings.GetOrNullAsync(CollectionSettings.Withdraw.MaxDailyAmount)
                ?? WithdrawDefaults.MaxDailyAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MaxDailyCount = await _settings.GetOrNullAsync(CollectionSettings.Withdraw.MaxDailyCount)
                ?? WithdrawDefaults.MaxDailyCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            AutoApproveMaxAmount = await _settings.GetOrNullAsync(CollectionSettings.Withdraw.AutoApproveMaxAmount)
                ?? WithdrawDefaults.AutoApproveMaxAmount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PendingCount = byStatus.TryGetValue(AbpAdmin.Collection.Entities.WithdrawStatus.Pending, out var p) ? p : 0,
            ProcessingCount = byStatus.TryGetValue(AbpAdmin.Collection.Entities.WithdrawStatus.Processing, out var pr) ? pr : 0,
            BroadcastingCount = byStatus.TryGetValue(AbpAdmin.Collection.Entities.WithdrawStatus.Broadcasting, out var b) ? b : 0,
            BroadcastFailedCount = byStatus.TryGetValue(AbpAdmin.Collection.Entities.WithdrawStatus.BroadcastFailed, out var bf) ? bf : 0,
        };
    }

    /// <summary>
    /// 配置提现热钱包（地址 + 私钥）：私钥与地址必须匹配（Nethereum 推导校验），
    /// 密文入库（版本前缀 + Collection:MasterKey），明文不落日志不回显。
    /// 与归集助记词必须不同源——泄露一个不应危及另一个。
    /// </summary>
    [Volo.Abp.Auditing.DisableAuditing] // 私钥不得进审计日志（AbpAuditLogActions.Parameters）；回归锁：E2E 轮2 实测曾因后续签名改写丢此属性致 6 行泄密
    public virtual async Task SetWithdrawVaultAsync(SetWithdrawVaultInput input)
    {
        AssertHost();
        var address = (input.Address ?? string.Empty).Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(address, "^0x[0-9a-fA-F]{40}$"))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidAddress).WithData("ToAddress", address);
        }

        var privateKey = (input.PrivateKey ?? string.Empty).Trim();
        string derived;
        try
        {
            var key = new Nethereum.Signer.EthECKey(privateKey);
            derived = key.GetPublicAddress();
        }
        catch (Exception)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount)
                .WithData("Reason", "private key malformed");
        }

        if (!string.Equals(derived, address, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidAddress)
                .WithData("Reason", "private key does not match the address");
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks.OrderBy(n => n.CreationTime).FirstAsync()
                      ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        network.WithdrawVaultAddress = address.ToLowerInvariant();
        network.WithdrawVaultKeyCipher = _guard.ProtectKey(privateKey);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>热钱包状态卡（余额/门槛/熔断；无密钥信息）。</summary>
    public virtual async Task<AbpAdmin.Collection.Withdrawals.WithdrawHotWalletStatusDto> GetHotWalletAsync()
    {
        AssertHost();
        var (_, gateway) = await CreateWithdrawGatewayAsync();
        return await _guard.GetStatusAsync(gateway);
    }

    /// <summary>
    /// 热钱包监控状态卡：各币游标/最近扫描/最近错误 + 未处置异常计数（前端状态卡与红点口径）。
    /// 不碰链（纯 DB 读）——余额/熔断走 GetHotWalletAsync。
    /// </summary>
    public virtual async Task<HotWalletWatchStatusDto> GetHotWalletWatchStatusAsync()
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var states = await dbContext.HotWalletWatchStates.AsNoTracking()
            .OrderBy(s => s.Symbol)
            .ToListAsync();
        var anomalyOpen = await dbContext.HotWalletTxns.AsNoTracking()
            .CountAsync(t => t.Status == HotWalletTxnStatus.Anomaly);
        await uow.CompleteAsync();

        return new HotWalletWatchStatusDto
        {
            AnomalyOpenCount = anomalyOpen,
            Tokens = states.Select(s => new HotWalletWatchTokenDto
            {
                Symbol = s.Symbol,
                LastScannedBlock = s.LastScannedBlock,
                LastScanAt = s.LastScanAt,
                LastError = s.LastError,
            }).ToList(),
        };
    }

    /// <summary>热钱包流水分页（可按状态/方向筛选，最新在前）。</summary>
    public virtual async Task<PagedResultDto<HotWalletTxnDto>> GetHotWalletTxnsAsync(
        int skipCount, int maxResultCount, string? status = null, string? direction = null)
    {
        AssertHost();
        if (maxResultCount <= 0 || maxResultCount > 200)
        {
            maxResultCount = 20;
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var query = dbContext.HotWalletTxns.AsNoTracking();
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<HotWalletTxnStatus>(status, true, out var st))
        {
            query = query.Where(t => t.Status == st);
        }
        if (!string.IsNullOrEmpty(direction) && Enum.TryParse<HotWalletDirection>(direction, true, out var dir))
        {
            query = query.Where(t => t.Direction == dir);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.BlockNumber).ThenByDescending(t => t.LogIndex)
            .Skip(Math.Max(0, skipCount))
            .Take(maxResultCount)
            .ToListAsync();
        await uow.CompleteAsync();

        return new PagedResultDto<HotWalletTxnDto>(total, items.Select(t => new HotWalletTxnDto
        {
            Id = t.Id,
            Symbol = t.Symbol,
            TxHash = t.TxHash,
            LogIndex = t.LogIndex,
            From = t.From,
            To = t.To,
            Direction = t.Direction.ToString(),
            Amount = t.Amount == null ? null : CollectionAmounts.Format(t.Amount.Value),
            RawAmount = t.RawAmount,
            BlockNumber = t.BlockNumber,
            Status = t.Status.ToString(),
            MatchedWithdrawId = t.MatchedWithdrawId,
            AckedBy = t.AckedBy,
            AckedAt = t.AckedAt,
            AckNote = t.AckNote,
            CreationTime = t.CreationTime,
        }).ToList());
    }

    /// <summary>
    /// 确认已知（Anomaly → Acked 终态）：管理员核实后的留痕动作（谁、何时、备注）。
    /// CAS：并发双击只有一个赢家；非 Anomaly 拒绝。备注限 512（列宽，超长给业务错而非 DB 500）。
    /// </summary>
    public virtual async Task AckHotWalletTxnAsync(Guid id, string? note = null)
    {
        AssertHost();
        if (note != null && note.Length > 512)
        {
            throw new BusinessException(CollectionErrorCodes.HotWalletTxnNotAckable)
                .WithData("Reason", "备注超长（≤512 字符）");
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var txn = await dbContext.HotWalletTxns.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (txn == null)
        {
            throw new BusinessException(CollectionErrorCodes.HotWalletTxnNotFound).WithData("HotWalletTxnId", id);
        }

        if (txn.Status != HotWalletTxnStatus.Anomaly)
        {
            throw new BusinessException(CollectionErrorCodes.HotWalletTxnNotAckable)
                .WithData("Reason", $"仅异常状态可确认（当前 {txn.Status}）");
        }

        var now = Clock.Now;
        var rows = await dbContext.HotWalletTxns
            .Where(t => t.Id == id && t.Status == HotWalletTxnStatus.Anomaly)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, HotWalletTxnStatus.Acked)
                .SetProperty(t => t.AckedBy, CurrentUser.UserName ?? "system")
                .SetProperty(t => t.AckedAt, now)
                .SetProperty(t => t.AckNote, note));
        if (rows == 0)
        {
            Logger.LogWarning("热钱包流水 {TxnId} 确认被并发处置（0 行），幂等返回", id);
        }

        await uow.CompleteAsync();
    }

    /// <summary>
    /// 提现总开关（资金面高危杠杆：开启后广播 worker 才会从热钱包付币）：写入设置库，
    /// 提交闸与广播 worker 每轮热读（下一轮生效免重启）。挂在 WithdrawAdmin 权限位——
    /// 此前经 SettingUi 通用设置页可改，评审定为权限错位（资金开关不交给通用设置更新权限）。
    /// </summary>
    public virtual async Task SetWithdrawEnabledAsync(bool enabled)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _settingManager.SetGlobalAsync(
            CollectionSettings.Withdraw.Enabled, enabled ? "true" : "false");
        await uow.CompleteAsync();
    }

    /// <summary>重试广播失败单（有哈希先验回执；无哈希过近期同地址付款双花门）。</summary>
    public virtual async Task RetryAsync(Guid id)
    {
        AssertHost();
        var (network, gateway) = await CreateWithdrawGatewayAsync();
        await _broadcast.RetryAsync(id, network, gateway);
    }

    /// <summary>
    /// 人工完结（BroadcastFailed 单补录链上哈希）：强制查回执且状态必须成功；
    /// 金额匹配核对依赖人工（回执不含金额解析，v1 以哈希+成功状态+人工确认为准）。
    /// </summary>
    public virtual async Task CompleteAsync(Guid id, string txHash)
    {
        AssertHost();
        txHash = (txHash ?? string.Empty).Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(txHash, "^0x[0-9a-f]{64}$"))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Reason", "tx hash malformed");
        }

        var (_, gateway) = await CreateWithdrawGatewayAsync();
        var receipt = await gateway.GetTransactionReceiptAsync(txHash);
        if (receipt.Status == null)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawPendingOutbound).WithData("TxHash", txHash);
        }

        if (receipt.Status != 1)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus)
                .WithData("Reason", "receipt status failed");
        }

        var order = await _withdraws.FindAsync(id);
        if (string.IsNullOrEmpty(order.TxHash))
        {
            // 无哈希补录：本端点的主场景。先 CAS 落哈希（BroadcastFailed/无哈希），再走完结；
            // 金额无法从回执自动核对（Transfer 事件解析不在此端点能力内），以哈希+成功回执+人工确认为准
            using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var written = await dbContext.WithdrawOrders
                .Where(o => o.Id == id
                            && (o.Status == WithdrawStatus.Broadcasting || o.Status == WithdrawStatus.BroadcastFailed)
                            && o.TxHash == null)
                .ExecuteUpdateAsync(x => x.SetProperty(o => o.TxHash, txHash));
            if (written == 0)
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus)
                    .WithData("Reason", "单据状态不符（非广播中/广播失败，或已有别的哈希）");
            }

            await uow.CompleteAsync();
        }

        await _broadcast.CompleteFromReceiptAsync(id, txHash);
    }

    /// <summary>
    /// 存量余额开账回填：对每个有余额的用户，把 ColBalance(可用+冻结) 与账本 2201 投影的差额
    /// 一次性过账入账本（BizRefId = opening:{userId}:{currency}，幂等可重跑）。
    /// 上线提现前执行一次；之后的新充值走归集确认同事务过账，不再产生差额。
    /// </summary>
    public virtual async Task<WithdrawOpeningBackfillResultDto> BackfillOpeningAsync()
    {
        AssertHost();
        using var guardUow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var guardContext = await _dbContextProvider.GetDbContextAsync();
        // 运行前提（上线前无活跃提现）从文档升级为守卫：Pending 的冻结额会被整额记进「可用」科目，
        // 之后拒绝/退款在账本侧解冻 2202 会把用户子户打成负数而失败（InsufficientBalance）
        var active = await guardContext.WithdrawOrders.AsNoTracking().CountAsync(
            o => o.Status == WithdrawStatus.Pending || o.Status == WithdrawStatus.Processing
                 || o.Status == WithdrawStatus.Broadcasting || o.Status == WithdrawStatus.BroadcastFailed);
        if (active > 0)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus)
                .WithData("Reason", $"存在 {active} 笔未终态提现单，开账回填须在无活跃单时执行");
        }

        var balances = await guardContext.Balances.AsNoTracking()
            .Where(b => b.Balance + b.Frozen > 0)
            .Select(b => new { b.UserId, b.Currency, Total = b.Balance + b.Frozen })
            .ToListAsync();

        var posted = 0;
        var skipped = 0;
        foreach (var b in balances)
        {
            var ledgerBalance = await _ledger.GetUserAvailableBalanceAsync(b.UserId, b.Currency);
            var diff = b.Total - ledgerBalance;
            if (diff <= 0)
            {
                skipped++; // 账本已不低于余额行（已回填过或已有过账），不动
                continue;
            }

            await _ledger.PostOpeningAsync(b.UserId, b.Currency, diff);
            posted++;
        }

        await guardUow.CompleteAsync();
        return new WithdrawOpeningBackfillResultDto { Users = balances.Count, Posted = posted, Skipped = skipped };
    }

    private void AssertHost()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }
}

/// <summary>提现配置快照（管理端状态卡）。</summary>
public class WithdrawStatusDto
{
    public bool Enabled { get; set; }

    public bool Halt { get; set; }

    public string MinAmount { get; set; } = default!;

    public string FeeRate { get; set; } = default!;

    public string MaxDailyAmount { get; set; } = default!;

    public string MaxDailyCount { get; set; } = default!;

    public string AutoApproveMaxAmount { get; set; } = default!;

    public int PendingCount { get; set; }

    public int ProcessingCount { get; set; }

    public int BroadcastingCount { get; set; }

    public int BroadcastFailedCount { get; set; }
}

/// <summary>存量开账回填结果。</summary>
public class WithdrawOpeningBackfillResultDto
{
    public int Users { get; set; }

    public int Posted { get; set; }

    public int Skipped { get; set; }
}
