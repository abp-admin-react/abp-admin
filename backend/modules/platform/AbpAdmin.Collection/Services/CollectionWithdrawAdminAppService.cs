using Microsoft.AspNetCore.Mvc;
using AbpAdmin.Grids;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
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
using Volo.Abp.DistributedLocking;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Encryption;
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
    public Guid? NetworkId { get; set; }
    public string Address { get; set; } = default!;

    /// <summary>可选二次确认地址；传入时必须与 Address 严格一致。</summary>
    public string? ConfirmAddress { get; set; }

    public string PrivateKey { get; set; } = default!;
}

public class PurgeStuckNonceInput
{
    /// <summary>GasSponsor 或 WithdrawHotWallet。</summary>
    public string Target { get; set; } = default!;

    public long Nonce { get; set; }

    public decimal? MinGasPriceGwei { get; set; }

    public string Reason { get; set; } = default!;

    public string? NetworkName { get; set; }

    /// <summary>强制覆盖当前仍处于 Fueling/Broadcasting 的活单占位（WithdrawHotWallet 还要求先拉响 Halt）。</summary>
    public bool ForceOverrideActiveOwner { get; set; }
}

public class PurgeStuckNonceResultDto
{
    public string Target { get; set; } = default!;
    public string SenderAddress { get; set; } = default!;
    public long Nonce { get; set; }
    public string GasPriceWei { get; set; } = default!;
    public string TxHash { get; set; } = default!;
}

public class WalletNonceInfoDto
{
    public bool Configured { get; set; }
    public string? Address { get; set; }
    public long LatestNonce { get; set; }
    public long PendingNonce { get; set; }
    public bool HasPendingGap { get; set; }
    public bool IsDelegated { get; set; }
    public List<ChainTxAttemptSummaryDto> RecentAttempts { get; set; } = new();
}

public class ChainTxAttemptSummaryDto
{
    public string Purpose { get; set; } = default!;
    public Guid? RefId { get; set; }
    public long Nonce { get; set; }
    public int AttemptNo { get; set; }
    public string TxHash { get; set; } = default!;
    public string GasPriceWei { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTime BroadcastAt { get; set; }
}

public class WalletNonceStatusDto
{
    public WalletNonceInfoDto GasSponsor { get; set; } = new();
    public WalletNonceInfoDto WithdrawHotWallet { get; set; } = new();
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

    /// <summary>Out / In / Self。</summary>
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
/// 提现管理端（审核/拒绝/退款/重试/人工完结、热钱包配置与监控、卡死 Nonce 诊断与清理）。
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
    private readonly IAbpDistributedLock _distributedLock;
    private readonly Volo.Abp.SettingManagement.ISettingManager _settingManager;
    private readonly ChainKeyProvider? _chainKeyProvider;
    private readonly IStringEncryptionService? _encryptor;
    private readonly ChainAlertService? _alerts;

    public CollectionWithdrawAdminAppService(
        WithdrawManager withdraws,
        CollectionLedgerPoster ledger,
        WithdrawGuard guard,
        WithdrawBroadcastManager broadcast,
        AbpAdmin.Collection.Chain.ChainGatewayFactory gatewayFactory,
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ISettingProvider settings,
        Volo.Abp.SettingManagement.ISettingManager settingManager,
        IAbpDistributedLock distributedLock,
        ChainKeyProvider? chainKeyProvider = null,
        IStringEncryptionService? encryptor = null,
        ChainAlertService? alerts = null)
    {
        _withdraws = withdraws;
        _ledger = ledger;
        _guard = guard;
        _broadcast = broadcast;
        _gatewayFactory = gatewayFactory;
        _dbContextProvider = dbContextProvider;
        _settings = settings;
        _settingManager = settingManager;
        _distributedLock = distributedLock;
        _chainKeyProvider = chainKeyProvider;
        _encryptor = encryptor;
        _alerts = alerts;
    }

    /// <summary>提现单列表（可按订单、状态、用户过滤，倒序分页）。</summary>
    public virtual Task<PagedResultDto<WithdrawOrderDto>> GetWithdrawalsAsync(
        int skipCount, int maxResultCount, string? status = null, Guid? userId = null, Guid? withdrawalId = null) =>
        ReadWithdrawalsPageAsync(null, skipCount, maxResultCount, status, userId, withdrawalId);

    [HttpPost("api/app/collection-withdraw-admin/withdrawals/query")]
    public virtual Task<PagedResultDto<WithdrawOrderDto>> QueryWithdrawalsAsync([FromBody] GridQueryInput grid, string? status = null, Guid? userId = null, Guid? withdrawalId = null) =>
        ReadWithdrawalsPageAsync(grid, grid.SkipCount, grid.MaxResultCount, status, userId, withdrawalId);

    private async Task<PagedResultDto<WithdrawOrderDto>> ReadWithdrawalsPageAsync(GridQueryInput? grid,
        int skipCount, int maxResultCount, string? status = null, Guid? userId = null, Guid? withdrawalId = null)
    {
        AssertHost();
        if (maxResultCount <= 0 || maxResultCount > 200)
        {
            maxResultCount = 20;
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var query = dbContext.WithdrawOrders.AsNoTracking();
        if (withdrawalId is { } id)
        {
            query = query.Where(o => o.Id == id);
        }

        if (!string.IsNullOrEmpty(status)
            && Enum.TryParse<AbpAdmin.Collection.Entities.WithdrawStatus>(status, true, out var st))
        {
            query = query.Where(o => o.Status == st);
        }

        if (userId.HasValue)
        {
            query = query.Where(o => o.UserId == userId.Value);
        }

        var definition = new GridQueryDefinition<ColWithdrawOrder>()
            .SearchableField("id", x => x.Id.ToString())
            .Field("creationTime", x => x.CreationTime)
            .SearchableField("userId", x => x.UserId.ToString())
            .SearchableEnumField("currency", x => x.Currency)
            .SearchableField("toAddress", x => x.ToAddress)
            .Field("amount", x => x.Amount)
            .Field("fee", x => x.Fee)
            .Field("actualAmount", x => x.ActualAmount)
            .SearchableEnumField("status", x => x.Status)
            .SearchableField("txHash", x => x.TxHash)
            .SearchableField("auditRemark", x => x.AuditRemark);
        if (grid != null) query = definition.Filter(query, grid);
        var total = await query.CountAsync();
        var rows = await (grid == null
            ? query.OrderByDescending(o => o.CreationTime)
            : definition.OrderBy(query, grid, "creationTime", true, x => x.Id))
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
    /// 提现运维端点统一的网关装配（收拢四处逐份拷贝、错误码不一的装配）。
    /// cap 沿用扫链的 NodeLogResultCap 但允许缺省 10000：提现路径只调 eth_*（nonce/余额/签名/回执），
    /// 不消费 getLogs 截断启发式——「无实测上限拒绝工作」是扫链侧的规则，此处豁免且不伪造测量语义。
    /// </summary>
    private async Task<(ColNetwork Network, IBscChainGateway Gateway)> CreateWithdrawGatewayAsync(string? networkName = null)
    {
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var networks = WithdrawGuard.NetworkQuery(dbContext).AsNoTracking();
        var network = (networkName == null
                ? await networks.FirstOrDefaultAsync()
                : await networks.FirstOrDefaultAsync(n => n.Name == networkName))
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
    /// 成功 → 引导走人工完结，回执未定 → 拒绝）。无哈希时须有同发送地址、同 nonce 的其他已确认交易。
    /// </summary>
    public virtual async Task RefundAsync(WithdrawAuditInput input)
    {
        AssertHost();
        var order = await _withdraws.FindAsync(input.Id);
        if (order.Status != WithdrawStatus.BroadcastFailed)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus);
        }

        var (_, gateway) = await CreateWithdrawGatewayAsync(order.NetworkName);

        if (!string.IsNullOrEmpty(order.TxHash))
        {
            var receipt = await gateway.GetTransactionReceiptAsync(order.TxHash);
            if (receipt.Status is not (0 or 1))
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
        else if (!await _withdraws.HasOtherCompletedAttemptAsync(order))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawPendingOutbound);
        }

        await _withdraws.RefundAfterApproveAsync(order, CurrentUser.Id!.Value, input.Remark!);
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
            // 展示兜底走 WithdrawDefaults 单源（避免三处手抄字符串各自漂移）
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

        if (!string.IsNullOrWhiteSpace(input.ConfirmAddress)
            && !string.Equals(input.ConfirmAddress.Trim(), address, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidAddress)
                .WithData("Reason", "confirm address does not match address");
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

        await using var broadcastLock = await _distributedLock.TryAcquireAsync(
            CollectionConsts.WithdrawBroadcastLockKey, timeout: TimeSpan.FromSeconds(5));
        if (broadcastLock == null)
        {
            throw new BusinessException(CollectionErrorCodes.TryAgain);
        }

        string? previousAddress;
        string networkName;
        using (var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var network = await WithdrawGuard.NetworkQuery(dbContext).FirstOrDefaultAsync()
                          ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
            if (input.NetworkId.HasValue && input.NetworkId != network.Id)
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawNetworkUnsupported);
            }

            var normalizedNew = address.ToLowerInvariant();
            var isDepositAddress = await dbContext.DepositAddresses.AsNoTracking()
                .AnyAsync(a => a.NetworkId == network.Id && !a.IsRetired && a.Address.ToLower() == normalizedNew);
            if (isDepositAddress)
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawInvalidAddress)
                    .WithData("Reason", "cannot configure a user deposit address as hot wallet");
            }

            if (!string.IsNullOrEmpty(network.WithdrawVaultAddress)
                && !string.Equals(network.WithdrawVaultAddress, normalizedNew, StringComparison.OrdinalIgnoreCase))
            {
                var hasBroadcasting = await dbContext.WithdrawOrders.AsNoTracking()
                    .AnyAsync(o => o.NetworkName == network.Name && o.Status == WithdrawStatus.Broadcasting);
                if (hasBroadcasting)
                {
                    throw new BusinessException(CollectionErrorCodes.WithdrawPendingOutbound)
                        .WithData("Reason", "存在在途广播提现单（Broadcasting），禁止轮换热钱包");
                }
            }

            // 尽力检查目标地址是否存在合约代码/EIP-7702 委托；无 RPC 配置时不阻断首次冷启动初始化
            try
            {
                var gateway = await _gatewayFactory.CreateAsync(dbContext, network, network.NodeLogResultCap ?? 10000);
                var code = await gateway.GetCodeAsync(normalizedNew);
                if (BscGasPricePolicy.HasDeployedCode(code))
                {
                    throw new BusinessException(CollectionErrorCodes.WithdrawInvalidAddress)
                        .WithData("Reason", "target address has deployed bytecode (EIP-7702/contract not allowed)");
                }
            }
            catch (BusinessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "轮换热钱包前 eth_getCode 预检跳过（网关不可用）: Address={Address}", normalizedNew);
            }

            previousAddress = network.WithdrawVaultAddress;
            networkName = network.Name;
            network.WithdrawVaultAddress = normalizedNew;
            network.WithdrawVaultKeyCipher = await _guard.ProtectKeyAsync(privateKey);
            await dbContext.SaveChangesAsync();
            await uow.CompleteAsync();
        }

        if (_alerts != null)
        {
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixWithdrawHotWallet,
                "提现热钱包配置变更",
                $"操作人 {CurrentUser.UserName ?? "system"} 更新了网络 {networkName} 热钱包：{previousAddress ?? "(未配置)"} → {address.ToLowerInvariant()}。");
        }
    }

    /// <summary>
    /// 热钱包状态卡（余额/门槛/熔断；无密钥信息）。只读展示面：全新环境 RPC/key 池
    /// 未配置时降级为 Configured=false（页面显示「未配置」），不抛 500——资金路径
    ///（审批/广播/重试）不走此降级，仍经网关工厂 fail-closed。
    /// </summary>
    public virtual async Task<AbpAdmin.Collection.Withdrawals.WithdrawHotWalletStatusDto> GetHotWalletAsync()
    {
        AssertHost();
        try
        {
            var (_, gateway) = await CreateWithdrawGatewayAsync();
            return await _guard.GetStatusAsync(gateway);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("无可用 RPC") || ex.Message.Contains("MasterKey"))
        {
            Logger.LogWarning("热钱包状态卡降级为未配置（网关组装失败，仅展示面）：{Message}", ex.Message);
            return new AbpAdmin.Collection.Withdrawals.WithdrawHotWalletStatusDto { Configured = false };
        }
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
    public virtual Task<PagedResultDto<HotWalletTxnDto>> GetHotWalletTxnsAsync(
        int skipCount, int maxResultCount, string? status = null, string? direction = null) =>
        ReadHotWalletTxnsPageAsync(null, skipCount, maxResultCount, status, direction);

    [HttpPost("api/app/collection-withdraw-admin/hot-wallet-txns/query")]
    public virtual Task<PagedResultDto<HotWalletTxnDto>> QueryHotWalletTxnsAsync([FromBody] GridQueryInput grid, string? status = null, string? direction = null) =>
        ReadHotWalletTxnsPageAsync(grid, grid.SkipCount, grid.MaxResultCount, status, direction);

    private async Task<PagedResultDto<HotWalletTxnDto>> ReadHotWalletTxnsPageAsync(GridQueryInput? grid,
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

        var definition = new GridQueryDefinition<ColHotWalletTxn>()
            .Field("creationTime", x => x.CreationTime)
            .SearchableField("symbol", x => x.Symbol)
            .SearchableEnumField("direction", x => x.Direction)
            .SearchableField("counterparty", x => x.Direction == HotWalletDirection.Out ? x.To : x.From)
            .Field("amount", x => x.Amount)
            .SearchableEnumField("status", x => x.Status)
            .SearchableField("matchedWithdrawId", x => x.MatchedWithdrawId.ToString())
            .SearchableField("txHash", x => x.TxHash)
            .Field("blockNumber", x => x.BlockNumber)
            .SearchableField("ackedBy", x => x.AckedBy);
        if (grid != null) query = definition.Filter(query, grid);
        var total = await query.CountAsync();
        var items = await (grid == null
            ? query.OrderByDescending(t => t.BlockNumber).ThenByDescending(t => t.LogIndex)
            : definition.OrderBy(query, grid, "blockNumber", true, x => x.Id))
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
    /// 资金开关不交给通用设置更新权限（权限错位）。
    /// </summary>
    public virtual async Task SetWithdrawEnabledAsync(bool enabled)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _settingManager.SetGlobalAsync(
            CollectionSettings.Withdraw.Enabled, enabled ? "true" : "false");
        await uow.CompleteAsync();
    }

    /// <summary>
    /// 提现总闸（Halt=紧急停机：拒绝新提交 + 阻断全部审批；广播中订单仍走卡单恢复处置）。
    /// 与 Enabled 同拦一条付币管线，同走 WithdrawAdmin 权限位与确认弹窗——
    /// Halt 不留在通用设置页——Text 控件 + 通用更新权限下，低权限者可松闸恢复付币，
    /// 且文本框敲 "True" 会静默不拉闸（fail-open 于停机语义）。
    /// 读侧 SubmitAsync/WithdrawBroadcastManager 照旧热读同一设置键。
    /// </summary>
    public virtual async Task SetWithdrawHaltAsync(bool halt)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _settingManager.SetGlobalAsync(
            CollectionSettings.Withdraw.Halt, halt ? "true" : "false");
        await uow.CompleteAsync();
    }

    /// <summary>热钱包异常自动拉响停机总闸开关（WithdrawAdmin 专用）。</summary>
    public virtual async Task SetWithdrawAutoHaltOnAnomalyAsync(bool enabled)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _settingManager.SetGlobalAsync(
            CollectionSettings.Withdraw.AutoHaltOnAnomaly, enabled ? "true" : "false");
        await uow.CompleteAsync();
    }

    /// <summary>查询 Gas Sponsor 与提现热钱包的 latest/pending nonce、EIP-7702 字节码与最近尝试记录。</summary>
    public virtual async Task<WalletNonceStatusDto> GetWalletNonceStatusAsync(string? networkName = null)
    {
        AssertHost();
        var (network, gateway) = await CreateWithdrawGatewayAsync(networkName);
        var masterKey = _chainKeyProvider != null ? await _chainKeyProvider.GetAsync() : null;

        string? gasAddress = null;
        if (!string.IsNullOrEmpty(network.MnemonicCipher) && masterKey != null && _encryptor != null)
        {
            using var mnemonic = HdKeyService.Unprotect(network.MnemonicCipher, masterKey, _encryptor);
            gasAddress = HdKeyService.DeriveAddress(mnemonic.Words, HdKeyService.GasAddressIndex).ToLowerInvariant();
        }

        var hotAddress = string.IsNullOrEmpty(network.WithdrawVaultAddress)
            ? null
            : network.WithdrawVaultAddress.ToLowerInvariant();

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var dto = new WalletNonceStatusDto
        {
            GasSponsor = await BuildNonceInfoAsync(dbContext, network.Id, gateway, gasAddress),
            WithdrawHotWallet = await BuildNonceInfoAsync(dbContext, network.Id, gateway, hotAddress),
        };
        await uow.CompleteAsync();
        return dto;
    }

    /// <summary>
    /// 管理员卡死 nonce 自转清理逃生舱：按目标取分布式锁（SweepLockKey / WithdrawBroadcastLockKey）并叠加 AccountSlot 锁，
    /// 经 VerifyDeployedCodeAsync 复核未遭 EIP-7702 委托，且仅允许针对 pending &gt; latest 且 Nonce == latest 的卡池队头发送高 gas 0-BNB 自转；
    /// 若该 nonce 正被活跃 Fueling/Broadcasting 单持有，默认拒绝（需 ForceOverrideActiveOwner，WithdrawHotWallet 还须先拉响 Withdraw.Halt）。
    /// </summary>
    public virtual async Task<PurgeStuckNonceResultDto> PurgeStuckNonceAsync(PurgeStuckNonceInput input)
    {
        AssertHost();
        if (string.IsNullOrWhiteSpace(input.Reason) || input.Reason.Trim().Length > 256)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawRemarkRequired)
                .WithData("Reason", "清理卡死 nonce 必须填写原因（≤256 字符）");
        }

        if (input.Target is not ("GasSponsor" or "WithdrawHotWallet"))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidAddress)
                .WithData("Target", input.Target);
        }

        var lockKey = input.Target == "GasSponsor"
            ? CollectionConsts.SweepLockKey
            : CollectionConsts.WithdrawBroadcastLockKey;
        await using var targetLock = await _distributedLock.TryAcquireAsync(
            lockKey, timeout: TimeSpan.FromSeconds(5));
        if (targetLock == null)
        {
            throw new BusinessException(CollectionErrorCodes.TryAgain);
        }

        var (network, gateway) = await CreateWithdrawGatewayAsync(input.NetworkName);
        var masterKey = (_chainKeyProvider != null ? await _chainKeyProvider.GetAsync() : null)
                        ?? throw new BusinessException(CollectionErrorCodes.MasterKeyMissing);
        if (_encryptor == null)
        {
            throw new BusinessException(CollectionErrorCodes.MasterKeyMissing);
        }

        string sender;
        string privateKey;
        if (input.Target == "GasSponsor")
        {
            if (string.IsNullOrEmpty(network.MnemonicCipher))
            {
                throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
            }

            using var mnemonic = HdKeyService.Unprotect(network.MnemonicCipher, masterKey, _encryptor);
            sender = HdKeyService.DeriveAddress(mnemonic.Words, HdKeyService.GasAddressIndex).ToLowerInvariant();
            privateKey = HdKeyService.DerivePrivateKey(mnemonic.Words, HdKeyService.GasAddressIndex);
        }
        else
        {
            if (string.IsNullOrEmpty(network.WithdrawVaultAddress) || string.IsNullOrEmpty(network.WithdrawVaultKeyCipher))
            {
                throw new BusinessException(CollectionErrorCodes.HotWalletNotConfigured);
            }

            sender = network.WithdrawVaultAddress.ToLowerInvariant();
            privateKey = HdKeyService.UnprotectPlain(network.WithdrawVaultKeyCipher, masterKey, _encryptor)
                         ?? throw new BusinessException(CollectionErrorCodes.HotWalletNotConfigured);
        }

        var (codeConfirmed, _, _) = await gateway.VerifyDeployedCodeAsync(sender);
        if (codeConfirmed)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidAddress)
                .WithData("Reason", "目标地址检测到非空字节码（EIP-7702/合约），禁止自动发送清理交易");
        }

        var latestNonce = await gateway.GetTransactionCountLatestAsync(sender);
        var pendingNonce = await gateway.GetTransactionCountAsync(sender);
        if (input.Nonce != latestNonce || pendingNonce <= latestNonce)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus)
                .WithData("Reason", $"仅支持清理卡池队头（要求 Nonce={latestNonce} 且 Pending({pendingNonce}) > Latest({latestNonce})）");
        }

        var networkGasWei = await gateway.GetGasPriceWeiAsync();
        var floorGasWei = BscGasPricePolicy.ReplacementGasWei(
            BscGasPricePolicy.ApplyBump(networkGasWei, 2), networkGasWei);
        var minGasWei = input.MinGasPriceGwei is > 0m and var minGwei
            ? BscGasPricePolicy.GweiToWei(minGwei)
            : BigInteger.Zero;
        var gasWei = floorGasWei >= minGasWei ? floorGasWei : minGasWei;

        var signed = await gateway.PrepareTransactionAsync(
            privateKey, sender, "", "0x0", network.ChainId, input.Nonce, gasWei);
        var txHash = signed.Hash.ToLowerInvariant();
        var now = Clock.Now;

        using (var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            await ChainTxSlotCoordinator.LockAccountSlotAsync(dbContext, network.Id, sender, System.Threading.CancellationToken.None);

            if (input.Target == "WithdrawHotWallet")
            {
                var activeOrder = await dbContext.WithdrawOrders.AsNoTracking()
                    .Where(o => o.NetworkName == network.Name
                                && o.Status == WithdrawStatus.Broadcasting
                                && o.BroadcastSenderAddress == sender
                                && o.NonceUsed == input.Nonce)
                    .Select(o => new { o.Id })
                    .FirstOrDefaultAsync();
                if (activeOrder != null)
                {
                    var isHalted = await _settings.IsTrueAsync(CollectionSettings.Withdraw.Halt);
                    if (!isHalted || !input.ForceOverrideActiveOwner)
                    {
                        throw new BusinessException(CollectionErrorCodes.WithdrawPendingOutbound)
                            .WithData("Reason", $"提现单 {activeOrder.Id} 正持有热钱包队头 nonce={input.Nonce}，请先拉响 Halt 并显式启用强制覆盖");
                    }
                }
            }
            else
            {
                var activeFuel = await dbContext.SweepTasks.AsNoTracking()
                    .Where(t => t.NetworkId == network.Id
                                && t.Status == SweepTaskStatus.Fueling
                                && t.GasNonceUsed == input.Nonce)
                    .Select(t => new { t.Id })
                    .FirstOrDefaultAsync();
                if (activeFuel != null && !input.ForceOverrideActiveOwner)
                {
                    throw new BusinessException(CollectionErrorCodes.WithdrawPendingOutbound)
                        .WithData("Reason", $"归集打油任务 {activeFuel.Id} 正持有 Gas Sponsor 队头 nonce={input.Nonce}，如需强制顶替请启用 ForceOverrideActiveOwner");
                }
            }

            if (!await dbContext.ChainTxAttempts.AsNoTracking().AnyAsync(a => a.NetworkId == network.Id && a.TxHash == txHash))
            {
                dbContext.ChainTxAttempts.Add(new ColChainTxAttempt(GuidGenerator.Create())
                {
                    NetworkId = network.Id,
                    Purpose = ChainTxPurpose.SponsorHeadSelfTransfer,
                    RefId = null,
                    SenderAddress = sender,
                    ToAddress = sender,
                    Nonce = input.Nonce,
                    AttemptNo = 1,
                    TxHash = txHash,
                    GasPriceWei = gasWei.ToString(CultureInfo.InvariantCulture),
                    GasLimit = BscGasPricePolicy.NativeTransferGasLimit,
                    RawSignedTx = signed.RawHex,
                    Status = ChainTxAttemptStatus.Signed,
                    BroadcastAt = now,
                    CreationTime = now,
                });
                await dbContext.SaveChangesAsync();
            }

            await uow.CompleteAsync();
        }

        try
        {
            await gateway.BroadcastTransactionAsync(signed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (ChainTxSlotCoordinator.IsDeterministicTxRejection(ex))
            {
                using var rejectUow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
                var rejectDb = await _dbContextProvider.GetDbContextAsync();
                await rejectDb.ChainTxAttempts
                    .Where(a => a.NetworkId == network.Id && a.TxHash == txHash)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(a => a.Status, ChainTxAttemptStatus.Rejected)
                        .SetProperty(a => a.RawSignedTx, (string?)null));
                await rejectUow.CompleteAsync();
            }

            throw;
        }

        using (var updateUow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            var updateDb = await _dbContextProvider.GetDbContextAsync();
            await updateDb.ChainTxAttempts
                .Where(a => a.NetworkId == network.Id && a.TxHash == txHash)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ChainTxAttemptStatus.Broadcasted));
            await updateUow.CompleteAsync();
        }

        if (_alerts != null)
        {
            await _alerts.RaiseAsync(CollectionConsts.AlertPrefixWithdrawHotWallet,
                $"管理员清理卡死 Nonce（{input.Target}）",
                $"操作人 {CurrentUser.UserName ?? "system"} 对 {input.Target}({sender}) nonce={input.Nonce} 发送 0-BNB 自转（Tx={txHash}, GasWei={gasWei}），原因：{input.Reason.Trim()}。");
        }

        return new PurgeStuckNonceResultDto
        {
            Target = input.Target,
            SenderAddress = sender,
            Nonce = input.Nonce,
            GasPriceWei = gasWei.ToString(CultureInfo.InvariantCulture),
            TxHash = txHash,
        };
    }

    private static async Task<WalletNonceInfoDto> BuildNonceInfoAsync(
        CollectionDbContext dbContext, Guid networkId, IBscChainGateway gateway, string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return new WalletNonceInfoDto { Configured = false };
        }

        var normalized = address.ToLowerInvariant();
        var latest = await gateway.GetTransactionCountLatestAsync(normalized);
        var pending = await gateway.GetTransactionCountAsync(normalized);
        var code = await gateway.GetCodeAsync(normalized);
        var attempts = await dbContext.ChainTxAttempts.AsNoTracking()
            .Where(a => a.NetworkId == networkId && a.SenderAddress == normalized)
            .OrderByDescending(a => a.CreationTime)
            .Take(10)
            .Select(a => new ChainTxAttemptSummaryDto
            {
                Purpose = a.Purpose.ToString(),
                RefId = a.RefId,
                Nonce = a.Nonce,
                AttemptNo = a.AttemptNo,
                TxHash = a.TxHash,
                GasPriceWei = a.GasPriceWei,
                Status = a.Status.ToString(),
                BroadcastAt = a.BroadcastAt,
            })
            .ToListAsync();

        return new WalletNonceInfoDto
        {
            Configured = true,
            Address = normalized,
            LatestNonce = latest,
            PendingNonce = pending,
            HasPendingGap = pending > latest,
            IsDelegated = BscGasPricePolicy.HasDeployedCode(code),
            RecentAttempts = attempts,
        };
    }

    /// <summary>重试广播失败单（有哈希先验回执；无哈希须具备原发送方同 nonce 已被另一笔已完结提现占用的链上证据）。</summary>
    public virtual async Task RetryAsync(Guid id)
    {
        AssertHost();
        var order = await _withdraws.FindAsync(id);
        var (network, gateway) = await CreateWithdrawGatewayAsync(order.NetworkName);
        await _broadcast.RetryAsync(id, network, gateway);
    }

    /// <summary>人工完结：已记录哈希直接查回执，补录哈希必须核对原广播和代币转账。</summary>
    public virtual async Task CompleteAsync(Guid id, string txHash)
    {
        AssertHost();
        txHash = (txHash ?? string.Empty).Trim().ToLowerInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(txHash, "^0x[0-9a-f]{64}$"))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Reason", "tx hash malformed");
        }

        var order = await _withdraws.FindAsync(id);
        if (order.Status == WithdrawStatus.Completed && order.TxHash == txHash)
        {
            return;
        }

        if (order.Status is not (WithdrawStatus.Broadcasting or WithdrawStatus.BroadcastFailed)
            || (order.TxHash != null && order.TxHash != txHash))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus);
        }

        var (network, gateway) = await CreateWithdrawGatewayAsync(order.NetworkName);
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

        if (order.TxHash != null)
        {
            await _broadcast.CompleteFromReceiptAsync(id, txHash, order);
            return;
        }

        var sender = WithdrawManager.RequireBroadcastSender(order);
        if (receipt.BlockNumber is not long block
            || await gateway.GetTransactionNonceForSenderAsync(txHash, sender) != order.NonceUsed)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawReceiptMismatch);
        }

        var symbol = CollectionLedgerPoster.CurrencyCode(order.Currency);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var token = await dbContext.Tokens.AsNoTracking()
            .FirstOrDefaultAsync(t => t.NetworkId == network.Id && t.Symbol == symbol)
            ?? throw new BusinessException(CollectionErrorCodes.WithdrawReceiptMismatch);
        var (logs, _) = await gateway.GetTransferLogsAsync(token.ContractAddress, block, block);
        var matchingTransfer = logs.Any(log =>
            log.BlockNumber == block
            && string.Equals(log.TxHash, txHash, StringComparison.OrdinalIgnoreCase)
            && string.Equals(log.From, sender, StringComparison.OrdinalIgnoreCase)
            && string.Equals(log.To, order.ToAddress, StringComparison.OrdinalIgnoreCase)
            && DepositScanManager.TryScaleRaw(log.RawAmount, token.Decimals, out _) == order.ActualAmount);
        if (!matchingTransfer)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawReceiptMismatch);
        }

        await _broadcast.CompleteFromReceiptAsync(id, txHash, order, attachVerifiedHash: true);
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
