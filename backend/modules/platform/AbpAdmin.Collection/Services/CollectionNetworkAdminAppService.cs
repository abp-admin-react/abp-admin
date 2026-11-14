using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Localization;
using AbpAdmin.Collection.Permissions;
using AbpAdmin.Collection.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

public class AddNetworkApiKeyInput
{
    public Guid NetworkId { get; set; }
    public string Provider { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string? Label { get; set; }
}

public class ConfigureNetworkInput
{
    private int? _estimatedArrivalMinutes;

    public Guid NetworkId { get; set; }
    public string? RpcUrl { get; set; }
    public int? NodeLogResultCap { get; set; }
    public string DepositFeeRate { get; set; } = default!;
    public string AutoCreditMaxAmount { get; set; } = default!;

    /// <summary>BSC 充值展示的预计到账分钟数。显式 null 恢复默认值。</summary>
    public int? EstimatedArrivalMinutes
    {
        get => _estimatedArrivalMinutes;
        set
        {
            _estimatedArrivalMinutes = value;
            HasEstimatedArrivalMinutes = true;
        }
    }

    internal bool HasEstimatedArrivalMinutes { get; private set; }
}

public class ConfigureAnkrInput
{
    private string? _group;

    public Guid NetworkId { get; set; }
    public string? PlatformAuthKey { get; set; }
    public bool ClearPlatformAuthKey { get; set; }
    public int? ScanIntervalSeconds { get; set; }

    public string? Group
    {
        get => _group;
        set
        {
            _group = value;
            HasGroup = true;
        }
    }

    internal bool HasGroup { get; private set; }
}

public record AnkrSettingsDto(string? PlatformAuthKeyMasked, string? Group, int ScanIntervalSeconds);

public record AnkrBalanceDto(string Status, string? Credits, string? BalanceUsd, DateTime? CheckedAt);

public class TestNodeCredentialInput
{
    private string? _group;

    public Guid NetworkId { get; set; }
    public string Provider { get; set; } = default!;
    public string? Key { get; set; }
    public Guid? KeyId { get; set; }
    public string? Group
    {
        get => _group;
        set
        {
            _group = value;
            HasGroup = true;
        }
    }

    internal bool HasGroup { get; private set; }
}

// Passed 表示服务商验证结果；拒绝凭据也正常返回 HTTP 200。余额字段仅在 Ankr Platform 验证成功时提供。
public record TestNodeCredentialDto(bool Passed, string Message, string? Credits = null, string? BalanceUsd = null);

public class ConfigureNetworkTokenInput
{
    public Guid NetworkId { get; set; }
    public Guid TokenId { get; set; }
    public string MinDeposit { get; set; } = default!;
    public bool IsEnabled { get; set; }
}

/// <summary>链网运行状态（含代币通道进度）。</summary>
public class NetworkStatusDto
{
    public Guid Id { get; set; }
    public string Network { get; set; } = default!;
    public long ChainId { get; set; }
    public bool HasMnemonic { get; set; }
    public string? RpcUrlMasked { get; set; }
    public int? NodeLogResultCap { get; set; }
    public long? LatestChainHead { get; set; }
    public DateTime? ScanHeartbeatAt { get; set; }
    public DateTime? SweepHeartbeatAt { get; set; }
    public DateTime? ScanBehindAlertedAt { get; set; }
    public DateTime? SweepStuckAlertedAt { get; set; }
    public DateTime? GasLowAlertedAt { get; set; }
    public bool ScanEnabled { get; set; }
    public bool SweepEnabled { get; set; }

    /// <summary>热钱包逐笔监控开关，由 SetChainSwitchesAsync 写入。</summary>
    public bool HotWalletWatchEnabled { get; set; }

    /// <summary>已确认、尚未挂归集任务且等待人工放行的充值单数。</summary>
    public int HeldDepositCount { get; set; }

    /// <summary>持有中进款毛额合计（十进制字符串）。</summary>
    public string HeldDepositTotal { get; set; } = default!;

    /// <summary>充值手续费率（网络级，[0,1]；建单时快照，改费率只影响新单）。</summary>
    public decimal DepositFeeRate { get; set; }

    /// <summary>BSC 充值展示的预计到账分钟数，其他网络为 null。</summary>
    public int? EstimatedArrivalMinutes { get; set; }

    /// <summary>大额放行门（毛额超过它的进款单停在 Confirmed 等放行；0 = 全部等放行；十进制字符串）。</summary>
    public string AutoCreditMaxAmount { get; set; } = default!;

    public List<TokenStatusDto> Tokens { get; set; } = new();
}

/// <summary>代币通道运行状态。</summary>
public class TokenStatusDto
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = default!;
    public string ContractAddress { get; set; } = default!;
    public string MinDeposit { get; set; } = default!;
    public string? VaultAddress { get; set; }
    public long? LastScannedBlock { get; set; }
    public DateTime? LastScanAt { get; set; }
    public bool IsEnabled { get; set; }
    /// <summary>落后块数（链头 - 游标），粗略健康信号。</summary>
    public long? BlocksBehind { get; set; }
}

/// <summary>进款单列表行。</summary>
public class DepositOrderListDto
{
    public Guid Id { get; set; }
    public string TxHash { get; set; } = default!;
    public int LogIndex { get; set; }
    public Guid UserId { get; set; }
    public string ToAddress { get; set; } = default!;
    public string FromAddress { get; set; } = default!;
    public string? Amount { get; set; }

    /// <summary>充值手续费（建单快照；历史单 null）。</summary>
    public string? Fee { get; set; }

    /// <summary>入账净额（建单快照；历史单 null=回退毛额）。</summary>
    public string? ActualAmount { get; set; }
    public string RawAmount { get; set; } = default!;
    public string Symbol { get; set; } = default!;
    /// <summary>来源网络名（ColNetwork.Name，如 "BSC"）——多链就绪的网络维度列。</summary>
    public string NetworkName { get; set; } = default!;
    public long BlockNumber { get; set; }
    public string Status { get; set; } = default!;
    public Guid? SweepTaskId { get; set; }
    public bool ManuallyReleased { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>归集任务列表行。</summary>
public class SweepTaskListDto
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = default!;
    public Guid UserId { get; set; }
    public string Address { get; set; } = default!;
    public int AddressIndex { get; set; }
    public string Amount { get; set; } = default!;
    public string Status { get; set; } = default!;
    public bool Retryable { get; set; }
    public string? FailReason { get; set; }
    public string? TxHash { get; set; }
    public long? NonceUsed { get; set; }
    public string? GasTxHash { get; set; }
    public DateTime StatusChangedAt { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>金库核对留痕行。</summary>
public class VaultCheckListDto
{
    public Guid Id { get; set; }
    public string Symbol { get; set; } = default!;
    public string VaultBalance { get; set; } = default!;
    public string UserTotal { get; set; } = default!;
    public string Difference { get; set; } = default!;
    public DateTime CheckedAt { get; set; }
}

/// <summary>扫链 API KEY 行（只回掩码：**** + 尾 4 位）。</summary>
public class ChainApiKeyDto
{
    public Guid Id { get; set; }
    /// <summary>RpcBsc / EtherscanV2。</summary>
    public string Provider { get; set; } = default!;
    public string? Label { get; set; }
    /// <summary>掩码（尾 4 位可辨识，明文不出库）。</summary>
    public string MaskedKey { get; set; } = default!;
    public bool IsEnabled { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public int ConsecutiveFailures { get; set; }
    public DateTime CreationTime { get; set; }
}

/// <summary>扫链 KEY 池全量（两类服务商各一列 + 回退来源可见度）。</summary>
public class ChainApiKeysDto
{
    public List<ChainApiKeyDto> RpcKeys { get; set; } = new();
    public List<ChainApiKeyDto> EtherscanKeys { get; set; } = new();
    /// <summary>RPC 池为空时实际使用的回退来源（网络行 RpcCipher 掩码）；null = 无回退。</summary>
    public string? RpcFallbackMasked { get; set; }
    /// <summary>Etherscan 池为空时的回退来源：宿主配置端点是否存在。</summary>
    public bool EtherscanFallbackPresent { get; set; }
}

/// <summary>
/// 宿主链网管理接口。配置和凭据测试需要 Network.Admin；运营查询也接受 Collections 权限。
/// 密钥保存在加密字段中。任务在下一轮读取后台保存的设置。
/// </summary>
[Authorize]
public class CollectionNetworkAdminAppService : ApplicationService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly IStringEncryptionService _encryptor;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly Volo.Abp.SettingManagement.ISettingManager _settingManager;
    private readonly IRepository<IdentityUser, Guid> _users;
    private readonly ChainKeyProvider _chainKeys;
    private readonly ChainRuntimeSettings _runtimeSettings;
    private readonly AnkrPlatformBalanceClient _ankrBalanceClient;
    private readonly ChainProviderProbe _providerProbe;

    public CollectionNetworkAdminAppService(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        IStringEncryptionService encryptor,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IAbpDistributedLock distributedLock,
        Volo.Abp.SettingManagement.ISettingManager settingManager,
        IRepository<IdentityUser, Guid> users,
        ChainKeyProvider chainKeys,
        ChainRuntimeSettings runtimeSettings,
        AnkrPlatformBalanceClient ankrBalanceClient,
        ChainProviderProbe providerProbe)
    {
        _dbContextProvider = dbContextProvider;
        _encryptor = encryptor;
        _configuration = configuration;
        _distributedLock = distributedLock;
        _settingManager = settingManager;
        _users = users;
        _chainKeys = chainKeys;
        _runtimeSettings = runtimeSettings;
        _ankrBalanceClient = ankrBalanceClient;
        _providerProbe = providerProbe;
        LocalizationResource = typeof(CollectionResource);
    }

    [Authorize(CollectionPermissions.Network.Admin)]
    [HttpGet("api/app/collection-network-admin/runtime-settings")]
    public virtual async Task<ChainRuntimeSettingsDto> GetRuntimeSettingsAsync()
    {
        AssertHost();
        var result = await _runtimeSettings.ReadAsync()
            ?? CollectionChainWorkerOptions.FromConfiguration(_configuration).ToRuntimeSettings();
        result.HasEncryptionKey = !string.IsNullOrWhiteSpace(await _chainKeys.GetAsync());
        return result;
    }

    /// <summary>保存整组运行设置。空名单覆盖部署收件人，设置从下一轮任务起生效。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    [HttpPut("api/app/collection-network-admin/runtime-settings")]
    public virtual async Task<ChainRuntimeSettingsDto> UpdateRuntimeSettingsAsync([FromBody] ChainRuntimeSettingsDto input)
    {
        AssertHost();
        var recipients = input.AlertRecipientUserIds;
        if (!ChainRuntimeSettings.IsValidBudget(input.ChainRoundBudgetSeconds)
            || !ChainRuntimeSettings.IsValidBudget(input.WithdrawRoundBudgetSeconds)
            || recipients == null || recipients.Length > ChainRuntimeSettings.MaxRecipients
            || recipients.Contains(Guid.Empty))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidChainRuntimeSettings);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var uniqueRecipients = recipients.Distinct().ToArray();
        if (uniqueRecipients.Length > 0)
        {
            var users = await _users.GetQueryableAsync();
            var activeHostCount = await users.CountAsync(user =>
                uniqueRecipients.Contains(user.Id) && user.TenantId == null && user.IsActive);
            if (activeHostCount != uniqueRecipients.Length)
            {
                throw new BusinessException(CollectionErrorCodes.InvalidChainRuntimeSettings);
            }
        }

        var settings = new ChainRuntimeSettingsDto
        {
            ChainRoundBudgetSeconds = input.ChainRoundBudgetSeconds,
            WithdrawRoundBudgetSeconds = input.WithdrawRoundBudgetSeconds,
            AlertRecipientUserIds = uniqueRecipients,
            UseLegacyExplorerFallback = input.UseLegacyExplorerFallback,
        };
        // Use the same transactional lock for first insert and subsequent updates across host instances.
        var db = await _dbContextProvider.GetDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('Collection:RuntimeSettings'))");
        var hasEncryptionKey = !string.IsNullOrWhiteSpace(await _chainKeys.GetAsync());
        await _settingManager.SetGlobalAsync(CollectionSettings.Chain.Runtime, JsonSerializer.Serialize(settings));
        await uow.CompleteAsync();
        settings.HasEncryptionKey = hasEncryptionKey;
        return settings;
    }

    /// <summary>首次生成加密密钥。已有密钥和已有密文保留原来的解密关系。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    [DisableAuditing]
    [HttpPost("api/app/collection-network-admin/initialize-encryption")]
    public virtual async Task InitializeEncryptionAsync()
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await _dbContextProvider.GetDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('Collection:InitializeEncryption'))");
        if (!string.IsNullOrWhiteSpace(await _chainKeys.GetAsync()))
        {
            await uow.CompleteAsync();
            return;
        }

        if (await db.Networks.AnyAsync(network => network.RpcCipher != null || network.MnemonicCipher != null
                || network.WithdrawVaultKeyCipher != null || network.AnkrPlatformAuthKeyCipher != null) || await db.ChainApiKeys.AnyAsync())
        {
            throw new BusinessException(CollectionErrorCodes.MasterKeyMissing);
        }

        var key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        await _settingManager.SetGlobalAsync(CollectionSettings.Chain.MasterKey, key);
        await uow.CompleteAsync();
    }

    // ── 查询 ─────────────────────────────────────────────────────────────────────────────

    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task<List<CollectionNetworkSummaryDto>> GetNetworksAsync()
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var result = await CollectionNetworkQueries.ListAsync(await _dbContextProvider.GetDbContextAsync());
        await uow.CompleteAsync();
        return result;
    }

    /// <summary>链网运行状态（含代币通道进度/告警冷却/开关状态）。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task<NetworkStatusDto> GetStatusAsync(Guid? networkId = null)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        // networkId 缺省取最早网络（单网络现状）；显式传入用于多网络/测试隔离
        var network = networkId.HasValue
            ? await dbContext.Networks.FirstOrDefaultAsync(n => n.Id == networkId.Value)
            : await dbContext.Networks.OrderBy(n => n.CreationTime).FirstOrDefaultAsync();
        if (network == null)
        {
            throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        }

        var tokens = await dbContext.Tokens
            .Where(t => t.NetworkId == network.Id)
            .OrderBy(t => t.Symbol)
            .ToListAsync();
        var head = network.LatestChainHead ?? 0;

        // 大额门持有敞口（Confirmed 且未挂归集任务）：运维状态卡一眼看到等放行的规模
        var held = await dbContext.DepositOrders.AsNoTracking()
            .Where(o => o.NetworkId == network.Id && o.Status == DepositOrderStatus.Confirmed
                        && o.SweepTaskId == null && !o.ManuallyReleased)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(o => o.Amount ?? 0m) })
            .FirstOrDefaultAsync();
        await uow.CompleteAsync();

        return new NetworkStatusDto
        {
            Id = network.Id,
            HeldDepositCount = held?.Count ?? 0,
            HeldDepositTotal = CollectionAmounts.Format(held?.Total ?? 0m),
            Network = network.Name,
            ChainId = network.ChainId,
            HasMnemonic = !string.IsNullOrEmpty(network.MnemonicCipher),
            RpcUrlMasked = await MaskUrlAsync(network.RpcCipher),
            NodeLogResultCap = network.NodeLogResultCap,
            LatestChainHead = network.LatestChainHead,
            ScanHeartbeatAt = network.ScanHeartbeatAt,
            SweepHeartbeatAt = network.SweepHeartbeatAt,
            ScanBehindAlertedAt = network.ScanBehindAlertedAt,
            SweepStuckAlertedAt = network.SweepStuckAlertedAt,
            GasLowAlertedAt = network.GasLowAlertedAt,
            ScanEnabled = await SettingProvider.IsTrueAsync(CollectionSettings.Chain.ScanEnabled),
            SweepEnabled = await SettingProvider.IsTrueAsync(CollectionSettings.Chain.SweepEnabled),
            HotWalletWatchEnabled = await SettingProvider.IsTrueAsync(CollectionSettings.Chain.HotWalletWatchEnabled),
            DepositFeeRate = network.DepositFeeRate,
            EstimatedArrivalMinutes = network.GetEstimatedArrivalMinutes(),
            AutoCreditMaxAmount = Contracts.CollectionAmounts.Format(network.AutoCreditMaxAmount),
            Tokens = tokens.Select(t => new TokenStatusDto
            {
                Id = t.Id,
                Symbol = t.Symbol,
                ContractAddress = t.ContractAddress,
                MinDeposit = t.MinDeposit,
                VaultAddress = t.VaultAddress,
                LastScannedBlock = t.LastScannedBlock,
                LastScanAt = t.LastScanAt,
                IsEnabled = t.IsEnabled,
                BlocksBehind = t.LastScannedBlock.HasValue && head > 0 ? head - t.LastScannedBlock.Value : null,
            }).ToList(),
        };
    }

    /// <summary>
    /// 更新显式传入的扫链、归集和热钱包监控开关。任务下一轮读取，写入权限为 Network.Admin。
    /// </summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task SetChainSwitchesAsync(
        bool? scanEnabled = null, bool? sweepEnabled = null, bool? hotWalletWatchEnabled = null)
    {
        AssertHost();
        if (scanEnabled == null && sweepEnabled == null && hotWalletWatchEnabled == null)
        {
            return; // 幂等空操作
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var settingManager = _settingManager;
        if (scanEnabled.HasValue)
        {
            await settingManager.SetGlobalAsync(CollectionSettings.Chain.ScanEnabled, scanEnabled.Value ? "true" : "false");
        }
        if (sweepEnabled.HasValue)
        {
            await settingManager.SetGlobalAsync(CollectionSettings.Chain.SweepEnabled, sweepEnabled.Value ? "true" : "false");
        }
        if (hotWalletWatchEnabled.HasValue)
        {
            await settingManager.SetGlobalAsync(
                CollectionSettings.Chain.HotWalletWatchEnabled, hotWalletWatchEnabled.Value ? "true" : "false");
        }

        await uow.CompleteAsync();
    }

    /// <summary>人工放行已确认的充值单，保存跨归集批次的放行资格。关联任务已存在时重复请求成功。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task ApproveLargeDepositAsync(Guid id)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await _dbContextProvider.GetDbContextAsync();
        var addressId = await db.DepositOrders.AsNoTracking().Where(o => o.Id == id)
            .Select(o => (Guid?)o.AddressId).SingleOrDefaultAsync();
        if (addressId == null)
        {
            throw new BusinessException(CollectionErrorCodes.CollectionNotFound).WithData("DepositOrderId", id);
        }

        await DepositSweepQueue.LockAddressAsync(db, addressId.Value);
        var order = await db.DepositOrders.SingleAsync(o => o.Id == id);
        if (order.SweepTaskId != null
            && order.Status is DepositOrderStatus.Confirmed or DepositOrderStatus.Sweeping or DepositOrderStatus.Swept)
        {
            // 已自动挂队但尚未激活时，仍记录收到的人工放行，供截尾重排使用。
            if (order.Status == DepositOrderStatus.Confirmed)
            {
                order.ManuallyReleased = true;
            }
            await uow.CompleteAsync();
            return;
        }

        if (order.Status != DepositOrderStatus.Confirmed)
        {
            throw new BusinessException(CollectionErrorCodes.DepositNotReleasable)
                .WithData("Reason", $"当前状态 {order.Status} 不可放行");
        }

        order.ManuallyReleased = true;
        await DepositSweepQueue.EnqueueAsync(db, new[] { order }, () => GuidGenerator.Create(), Clock.Now);
        await uow.CompleteAsync();
    }

    /// <summary>
    /// 将失败任务重新排队并跳过冷却，下一次失败由 worker 重新判定能否自动重试。
    /// 条件更新避免覆盖其他请求已推进的状态，关联充值单仍处于 Sweeping。
    /// </summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task RetrySweepTaskAsync(Guid id)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var task = await dbContext.SweepTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (task == null)
        {
            throw new BusinessException(CollectionErrorCodes.SweepTaskNotFound).WithData("SweepTaskId", id);
        }

        if (task.Status != SweepTaskStatus.Failed)
        {
            throw new BusinessException(CollectionErrorCodes.SweepTaskNotRetryable)
                .WithData("Reason", $"仅 Failed 任务可重试（当前 {task.Status}）");
        }

        var now = Clock.Now;
        var rows = await dbContext.SweepTasks
            .Where(t => t.Id == id && t.Status == SweepTaskStatus.Failed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, SweepTaskStatus.Queued)
                .SetProperty(t => t.Retryable, false)
                .SetProperty(t => t.FailReason, (string?)null)
                .SetProperty(t => t.StatusChangedAt, now));
        if (rows == 0)
        {
            Logger.LogDebug("归集任务 {SweepTaskId} 状态已变化，本次未重新排队", id);
        }

        await uow.CompleteAsync();
        if (rows != 0)
        {
            Logger.LogInformation("归集任务 {SweepTaskId} 已手动重新排队，原失败原因：{FailReason}",
                id, task.FailReason);
        }
    }

    public virtual async Task<PagedResultDto<DepositOrderListDto>> GetDepositOrdersAsync(
        int skipCount, int maxResultCount, string? status = null, string? symbol = null, Guid? depositId = null)
    {
        await CheckOperationsReadAccessAsync();
        if (maxResultCount <= 0 || maxResultCount > 200) maxResultCount = 20;
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var symbols = await dbContext.Tokens.ToDictionaryAsync(t => t.Id, t => t.Symbol);
        var networkNames = await dbContext.Networks.ToDictionaryAsync(n => n.Id, n => n.Name);
        var query = dbContext.DepositOrders.AsNoTracking();

        if (depositId is { } id)
        {
            query = query.Where(o => o.Id == id);
        }

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<DepositOrderStatus>(status, true, out var st))
        {
            query = query.Where(o => o.Status == st);
        }
        if (!string.IsNullOrEmpty(symbol))
        {
            var tokenIds = symbols.Where(kv => kv.Value.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                .Select(kv => kv.Key).ToList();
            query = query.Where(o => tokenIds.Contains(o.TokenId));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(o => o.CreationTime)
            .Skip(Math.Max(0, skipCount))
            .Take(maxResultCount)
            .ToListAsync();
        await uow.CompleteAsync();

        return new PagedResultDto<DepositOrderListDto>(total, items.Select(o => new DepositOrderListDto
        {
            Id = o.Id,
            TxHash = o.TxHash,
            LogIndex = o.LogIndex,
            UserId = o.UserId,
            ToAddress = o.ToAddress,
            FromAddress = o.FromAddress,
            Amount = o.Amount == null ? null : CollectionAmounts.Format(o.Amount.Value),
            Fee = o.Fee == null ? null : CollectionAmounts.Format(o.Fee.Value),
            ActualAmount = o.ActualAmount == null ? null : CollectionAmounts.Format(o.ActualAmount.Value),
            RawAmount = o.RawAmount,
            Symbol = symbols.TryGetValue(o.TokenId, out var s) ? s : "?",
            NetworkName = networkNames.TryGetValue(o.NetworkId, out var n) ? n : "?",
            BlockNumber = o.BlockNumber,
            Status = o.Status.ToString(),
            SweepTaskId = o.SweepTaskId,
            ManuallyReleased = o.ManuallyReleased,
            CreationTime = o.CreationTime,
        }).ToList());
    }

    /// <summary>归集任务分页。</summary>
    public virtual async Task<PagedResultDto<SweepTaskListDto>> GetSweepTasksAsync(
        int skipCount, int maxResultCount, string? status = null)
    {
        await CheckOperationsReadAccessAsync();
        if (maxResultCount <= 0 || maxResultCount > 200) maxResultCount = 20;
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var symbols = await dbContext.Tokens.ToDictionaryAsync(t => t.Id, t => t.Symbol);
        var query = dbContext.SweepTasks.AsNoTracking();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<SweepTaskStatus>(status, true, out var st))
        {
            query = query.Where(t => t.Status == st);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.CreationTime)
            .Skip(Math.Max(0, skipCount))
            .Take(maxResultCount)
            .ToListAsync();
        await uow.CompleteAsync();

        return new PagedResultDto<SweepTaskListDto>(total, items.Select(t => new SweepTaskListDto
        {
            Id = t.Id,
            Symbol = symbols.TryGetValue(t.TokenId, out var s) ? s : "?",
            UserId = t.UserId,
            Address = t.Address,
            AddressIndex = t.AddressIndex,
            Amount = CollectionAmounts.Format(t.Amount),
            Status = t.Status.ToString(),
            Retryable = t.Retryable,
            FailReason = t.FailReason,
            TxHash = t.TxHash,
            NonceUsed = t.NonceUsed,
            GasTxHash = t.GasTxHash,
            StatusChangedAt = t.StatusChangedAt,
            CreationTime = t.CreationTime,
        }).ToList());
    }

    /// <summary>金库核对留痕分页。</summary>
    public virtual async Task<PagedResultDto<VaultCheckListDto>> GetVaultChecksAsync(
        int skipCount, int maxResultCount)
    {
        await CheckOperationsReadAccessAsync();
        if (maxResultCount <= 0 || maxResultCount > 200) maxResultCount = 20;
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var query = dbContext.VaultChecks.AsNoTracking();

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(v => v.CheckedAt)
            .Skip(Math.Max(0, skipCount))
            .Take(maxResultCount)
            .ToListAsync();
        await uow.CompleteAsync();

        return new PagedResultDto<VaultCheckListDto>(total, items.Select(v => new VaultCheckListDto
        {
            Id = v.Id,
            Symbol = v.Symbol,
            VaultBalance = CollectionAmounts.Format(v.VaultBalance),
            UserTotal = CollectionAmounts.Format(v.UserTotal),
            Difference = CollectionAmounts.Format(v.Difference),
            CheckedAt = v.CheckedAt,
        }).ToList());
    }

    // ── 写入 ─────────────────────────────────────────────────────────────────────────────

    [Authorize(CollectionPermissions.Network.Admin)]
    [HttpGet("api/app/collection-network-admin/ankr-settings")]
    public virtual async Task<AnkrSettingsDto> GetAnkrSettingsAsync(Guid? networkId = null)
    {
        AssertHost();
        var db = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireAnkrNetworkAsync(db, networkId);
        var interval = await SettingProvider.GetScanIntervalSecondsAsync();
        var masked = network.AnkrPlatformAuthKeyCipher != null
            ? MaskApiKey(await ReadProviderKeyAsync(network.AnkrPlatformAuthKeyCipher))
            : null;
        return new AnkrSettingsDto(masked, network.AnkrGroup, interval);
    }

    [Authorize(CollectionPermissions.Network.Admin)]
    [DisableAuditing]
    [HttpPost("api/app/collection-network-admin/configure-ankr")]
    public virtual async Task ConfigureAnkrAsync([FromBody] ConfigureAnkrInput input)
    {
        AssertHost();
        var key = string.IsNullOrWhiteSpace(input.PlatformAuthKey) ? null : input.PlatformAuthKey.Trim();
        var group = string.IsNullOrWhiteSpace(input.Group) ? null : input.Group.Trim();
        if ((input.ClearPlatformAuthKey && key != null) || !IsValidPlatformKey(key) || !IsValidAnkrGroup(group)
            || input.ScanIntervalSeconds is < ChainDefaults.MinIntervalSeconds or > ChainDefaults.MaxScanIntervalSeconds)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAnkrSettings);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await _dbContextProvider.GetDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('Collection:AnkrSettings'))");
        var network = await RequireAnkrNetworkAsync(db, input.NetworkId);
        if (key != null)
        {
            network.AnkrPlatformAuthKeyCipher = HdKeyService.Protect(key, await RequireMasterKeyAsync(), _encryptor);
        }
        else if (input.ClearPlatformAuthKey)
        {
            network.AnkrPlatformAuthKeyCipher = null;
        }
        if (input.HasGroup)
        {
            network.AnkrGroup = group;
        }
        if (input.ScanIntervalSeconds is int seconds)
        {
            await _settingManager.SetGlobalAsync(CollectionSettings.Chain.ScanIntervalSeconds,
                seconds.ToString(CultureInfo.InvariantCulture));
        }

        await db.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    [HttpGet("api/app/collection-network-admin/ankr-balance")]
    public virtual async Task<AnkrBalanceDto> GetAnkrBalanceAsync(Guid? networkId = null, CancellationToken cancellationToken = default)
    {
        await CheckOperationsReadAccessAsync();
        var db = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireAnkrNetworkAsync(db, networkId);
        if (network.AnkrPlatformAuthKeyCipher == null)
        {
            return new AnkrBalanceDto("notConfigured", null, null, null);
        }

        var key = await ReadProviderKeyAsync(network.AnkrPlatformAuthKeyCipher);
        var result = key == null ? null : await _ankrBalanceClient.ReadAsync(key, network.AnkrGroup, cancellationToken);
        return result is { } balance
            ? new AnkrBalanceDto("available", balance.Credits, balance.BalanceUsd, Clock.Now)
            : new AnkrBalanceDto("unavailable", null, null, Clock.Now);
    }

    [Authorize(CollectionPermissions.Network.Admin)]
    [DisableAuditing]
    [UnitOfWork(isTransactional: false)]
    [HttpPost("api/app/collection-network-admin/test-node-credential")]
    public virtual async Task<TestNodeCredentialDto> TestNodeCredentialAsync(
        [FromBody] TestNodeCredentialInput input, CancellationToken cancellationToken = default)
    {
        AssertHost();
        var key = string.IsNullOrWhiteSpace(input.Key) ? null : input.Key.Trim();
        var provider = input.Provider switch
        {
            "RpcBsc" => ChainApiKeyProvider.RpcBsc,
            "EtherscanV2" => ChainApiKeyProvider.EtherscanV2,
            _ => (ChainApiKeyProvider?)null,
        };
        var isPlatform = input.Provider == "AnkrPlatform";
        var group = string.IsNullOrWhiteSpace(input.Group) ? null : input.Group.Trim();
        if (input.NetworkId == Guid.Empty || (!isPlatform && !provider.HasValue) || input.KeyId == Guid.Empty
            || (input.KeyId.HasValue && (isPlatform || key != null))
            || (isPlatform && (!IsValidPlatformKey(key) || !IsValidAnkrGroup(group)))
            || (provider is { } keyProvider && (key == null ? !input.KeyId.HasValue : !IsValidKeyShape(keyProvider, key))))
        {
            return new TestNodeCredentialDto(false, L["NodeProbeInvalidInput"]);
        }

        var db = await _dbContextProvider.GetDbContextAsync();
        var network = await db.Networks.AsNoTracking()
            .Where(n => n.Id == input.NetworkId && n.Name == CollectionConsts.BscNetworkName && n.ChainId == CollectionConsts.BscMainnetChainId)
            .Select(n => new { n.AnkrPlatformAuthKeyCipher, n.AnkrGroup })
            .SingleOrDefaultAsync(cancellationToken);
        if (network == null)
        {
            return new TestNodeCredentialDto(false, L["NodeProbeUnsupportedNetwork"]);
        }

        if (isPlatform)
        {
            group = input.HasGroup ? group : network.AnkrGroup;
            if (key == null)
            {
                key = await ReadProviderKeyAsync(network.AnkrPlatformAuthKeyCipher);
            }
            if (!IsValidPlatformKey(key) || !IsValidAnkrGroup(group))
            {
                return new TestNodeCredentialDto(false, L["NodeProbeInvalidInput"]);
            }
        }
        else if (input.KeyId is { } keyId)
        {
            var cipher = await db.ChainApiKeys.AsNoTracking()
                .Where(k => k.Id == keyId && k.NetworkId == input.NetworkId && k.Provider == provider)
                .Select(k => k.KeyCipher)
                .SingleOrDefaultAsync(cancellationToken);
            key = await ReadProviderKeyAsync(cipher);
            if (key != null && provider is { } savedProvider && !IsValidKeyShape(savedProvider, key))
            {
                key = null;
            }
        }

        if (key == null)
        {
            return new TestNodeCredentialDto(false, L["NodeProbeKeyUnavailable"]);
        }

        if (isPlatform)
        {
            var (status, balance) = await _ankrBalanceClient.TestAsync(key, group, cancellationToken);
            if (balance is { } available)
            {
                return new TestNodeCredentialDto(true, L["NodeProbeAnkrPlatformAvailable"], available.Credits, available.BalanceUsd);
            }

            return new TestNodeCredentialDto(false, status == ProviderCredentialStatus.Invalid
                ? L["NodeProbeAnkrPlatformRejected"] : L["NodeProbeUnavailable"]);
        }

        var result = provider == ChainApiKeyProvider.RpcBsc ? await _providerProbe.TestAnkrRpcAsync(key, cancellationToken)
            : await _providerProbe.TestEtherscanAsync(key, cancellationToken);
        var message = result switch
        {
            ProviderCredentialStatus.Available when provider == ChainApiKeyProvider.RpcBsc => L["NodeProbeAnkrRpcAvailable"],
            ProviderCredentialStatus.Available => L["NodeProbeEtherscanAvailable"],
            ProviderCredentialStatus.PlanRequired => L["NodeProbeEtherscanPlanRequired"],
            ProviderCredentialStatus.Invalid when provider == ChainApiKeyProvider.EtherscanV2 => L["NodeProbeEtherscanRejected"],
            ProviderCredentialStatus.Invalid => L["NodeProbeRejected"],
            _ => L["NodeProbeUnavailable"],
        };
        return new TestNodeCredentialDto(result == ProviderCredentialStatus.Available, message);
    }

    [Authorize(CollectionPermissions.Network.Admin)]
    [DisableAuditing]
    public virtual async Task ConfigureNetworkAsync(ConfigureNetworkInput input)
    {
        AssertHost();
        var fee = CollectionAmounts.Parse(input.DepositFeeRate);
        var maximum = CollectionAmounts.Parse(input.AutoCreditMaxAmount);
        if (fee < 0m || fee > 1m || maximum < 0m || input.NodeLogResultCap is < 100)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount);
        }
        if (!string.IsNullOrWhiteSpace(input.RpcUrl) &&
            !ChainHttpTransport.IsAllowedEndpoint(input.RpcUrl.Trim()))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidRpcUrl);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireNetworkAsync(db, input.NetworkId);
        if (input.HasEstimatedArrivalMinutes && network.IsBscMainnet)
        {
            if (input.EstimatedArrivalMinutes is < 1 or > CollectionConsts.MaxEstimatedArrivalMinutes)
            {
                throw new BusinessException(CollectionErrorCodes.InvalidEstimatedArrivalMinutes);
            }
            network.EstimatedArrivalMinutes = input.EstimatedArrivalMinutes;
        }
        else if (input.EstimatedArrivalMinutes != null)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidEstimatedArrivalMinutes);
        }
        network.DepositFeeRate = fee;
        network.AutoCreditMaxAmount = maximum;
        network.NodeLogResultCap = input.NodeLogResultCap;
        if (!string.IsNullOrWhiteSpace(input.RpcUrl))
        {
            network.RpcCipher = '\u0001' + _encryptor.Encrypt(input.RpcUrl.Trim(), await RequireMasterKeyAsync());
        }
        await db.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task ConfigureTokenAsync(ConfigureNetworkTokenInput input)
    {
        AssertHost();
        var minimum = CollectionAmounts.Parse(input.MinDeposit);
        if (minimum <= 0m)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount);
        }
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var db = await _dbContextProvider.GetDbContextAsync();
        var token = await db.Tokens.FirstOrDefaultAsync(row => row.Id == input.TokenId && row.NetworkId == input.NetworkId)
                    ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
        token.MinDeposit = CollectionAmounts.Format(minimum);
        token.IsEnabled = input.IsEnabled;
        await db.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>一次性生成助记词（只展示一次；已有地址或未完成归集则拒绝）。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    [DisableAuditing]
    public virtual async Task<string> GenerateMnemonicAsync(Guid? networkId = null)
    {
        AssertHost();
        await using var mnemonicLock = await _distributedLock.TryAcquireAsync(
            CollectionConsts.MnemonicLockKey, timeout: TimeSpan.FromSeconds(5));
        if (mnemonicLock == null)
        {
            throw new BusinessException(CollectionErrorCodes.TryAgain).WithData("Action", "助记词操作进行中，请稍后重试");
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireNetworkAsync(dbContext, networkId);
        var masterKey = await RequireMasterKeyAsync();

        if (!string.IsNullOrEmpty(network.MnemonicCipher))
        {
            var hasAddresses = await dbContext.DepositAddresses.AnyAsync(a => a.NetworkId == network.Id);
            var hasActiveSweeps = await dbContext.SweepTasks.AnyAsync(t =>
                t.NetworkId == network.Id
                && (t.Status == SweepTaskStatus.Queued || t.Status == SweepTaskStatus.Fueling || t.Status == SweepTaskStatus.Sweeping));
            if (hasAddresses || hasActiveSweeps)
            {
                throw new BusinessException(CollectionErrorCodes.MnemonicResetRefused)
                    .WithData("Reason", "已存在充值地址或未完成归集，重置助记词会导致资产不可达。");
            }
        }

        var words = HdKeyService.GenerateMnemonic();
        network.MnemonicCipher = HdKeyService.Protect(words, masterKey, _encryptor);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return words;
    }

    /// <summary>保存 RPC 单次日志结果上限，用于扫链时划分查询区间。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task SetNodeLogResultCapAsync(int cap, Guid? networkId = null)
    {
        AssertHost();
        if (cap < 100)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidCap).WithData("Cap", cap);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireNetworkAsync(dbContext, networkId);
        network.NodeLogResultCap = cap;
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>设置金库地址。缺少地址快照的历史广播结清前保留原配置，确保回执仍可核对。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task SetVaultAddressAsync(string symbol, string vaultAddress, Guid? networkId = null)
    {
        AssertHost();
        if (string.IsNullOrWhiteSpace(vaultAddress) || !Regex.IsMatch(vaultAddress, "^0x[0-9a-fA-F]{40}$"))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidVaultAddress).WithData("VaultAddress", vaultAddress);
        }

        await using var sweepLock = await _distributedLock.TryAcquireAsync(
            CollectionConsts.SweepLockKey, timeout: TimeSpan.FromSeconds(5));
        if (sweepLock == null)
        {
            throw new BusinessException(CollectionErrorCodes.TryAgain);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var token = await RequireTokenAsync(dbContext, symbol, networkId);
        if (!string.Equals(token.VaultAddress, vaultAddress, StringComparison.OrdinalIgnoreCase)
            && await dbContext.SweepTasks.AnyAsync(t => t.TokenId == token.Id
                && t.Status != SweepTaskStatus.Swept && t.BroadcastVaultAddress == null
                && (t.TxHash != null || t.NonceUsed != null || t.BroadcastAt != null)))
        {
            throw new BusinessException(CollectionErrorCodes.VaultHasLegacySweep);
        }
        token.VaultAddress = vaultAddress;
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>
    /// 保存充值费率（0 至 1）和自动归集额度（非负，0 表示全部等待放行）。
    /// 费率在建单时保存快照，调整只影响新单的手续费。
    /// </summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task SetDepositPolicyAsync(decimal feeRate, decimal autoCreditMaxAmount, Guid? networkId = null)
    {
        AssertHost();
        if (feeRate < 0m || feeRate > 1m)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount)
                .WithData("Reason", $"DepositFeeRate 必须在 [0,1]：{feeRate}");
        }

        if (autoCreditMaxAmount < 0m)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount)
                .WithData("Reason", $"AutoCreditMaxAmount 必须 ≥ 0（0=全部等放行）：{autoCreditMaxAmount}");
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireNetworkAsync(dbContext, networkId);
        network.DepositFeeRate = feeRate;
        network.AutoCreditMaxAmount = autoCreditMaxAmount;
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>设置最低充值额（正数；负数/零会让粉尘过滤失效）。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task SetMinDepositAsync(string symbol, string minDeposit, Guid? networkId = null)
    {
        AssertHost();
        var parsed = CollectionAmounts.Parse(minDeposit);
        if (parsed <= 0)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("MinDeposit", minDeposit);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var token = await RequireTokenAsync(dbContext, symbol, networkId);
        token.MinDeposit = minDeposit;
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>切换代币通道启用（管理端控制某代币是否参与扫链/归集）。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task SetTokenEnabledAsync(string symbol, bool isEnabled, Guid? networkId = null)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var token = await RequireTokenAsync(dbContext, symbol, networkId);
        token.IsEnabled = isEnabled;
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    // ── 扫链 KEY 池（同服务商多把，网关遇限流/认证错误自动轮换）──────────────────────────

    /// <summary>KEY 池全量（掩码回显，明文不出库；附回退来源可见度）。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task<ChainApiKeysDto> GetApiKeysAsync(Guid? networkId = null)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireNetworkAsync(dbContext, networkId);
        var keys = await dbContext.ChainApiKeys.AsNoTracking()
            .Where(k => k.NetworkId == network.Id)
            .OrderBy(k => k.Provider).ThenBy(k => k.CreationTime)
            .ToListAsync();
        await uow.CompleteAsync();

        var masterKey = keys.Count == 0 ? string.Empty : await RequireMasterKeyAsync();
        return new ChainApiKeysDto
        {
            RpcKeys = keys.Where(k => k.Provider == ChainApiKeyProvider.RpcBsc)
                .Select(k => ToApiKeyDto(k, masterKey)).ToList(),
            EtherscanKeys = keys.Where(k => k.Provider == ChainApiKeyProvider.EtherscanV2)
                .Select(k => ToApiKeyDto(k, masterKey)).ToList(),
            RpcFallbackMasked = await MaskUrlAsync(network.RpcCipher),
            EtherscanFallbackPresent = !string.IsNullOrEmpty(_configuration[CollectionSettings.Host.EtherscanV2Endpoint])
                && (await _runtimeSettings.ReadAsync())?.UseLegacyExplorerFallback != false,
        };
    }

    /// <summary>从请求体添加网络密钥，按网络和服务商判重，响应仅返回掩码。</summary>
    /// <param name="input">明确的网络 ID、服务商及待加密密钥。</param>
    /// <returns>不含明文的密钥配置。</returns>
    /// <exception cref="BusinessException">网络不存在、密钥格式错误或已存在。</exception>
    [Authorize(CollectionPermissions.Network.Admin)]
    [DisableAuditing]
    [HttpPost("api/app/collection-network-admin/add-api-key")]
    public virtual async Task<ChainApiKeyDto> AddApiKeyFromBodyAsync([FromBody] AddNetworkApiKeyInput input)
    {
        AssertHost();
        var provider = input.Provider;
        var key = input.Key;
        var label = input.Label;
        if (!Enum.TryParse<ChainApiKeyProvider>(provider, true, out var providerType))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidApiKey).WithData("Provider", provider);
        }

        var trimmed = key?.Trim() ?? string.Empty;
        if (!IsValidKeyShape(providerType, trimmed))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidApiKey)
                .WithData("Provider", provider)
                .WithData("KeyLength", trimmed.Length);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireNetworkAsync(dbContext, input.NetworkId);

        var keyHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(trimmed))).ToLowerInvariant();
        var duplicate = await dbContext.ChainApiKeys.AnyAsync(k =>
            k.NetworkId == network.Id && k.Provider == providerType && k.KeyHash == keyHash);
        if (duplicate)
        {
            throw new BusinessException(CollectionErrorCodes.ApiKeyExists).WithData("Provider", provider);
        }

        var masterKey = await RequireMasterKeyAsync();
        var row = new ColChainApiKey(GuidGenerator.Create())
        {
            NetworkId = network.Id,
            Provider = providerType,
            Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim(),
            KeyHash = keyHash,
            KeyCipher = '\u0001' + _encryptor.Encrypt(trimmed, masterKey),
            IsEnabled = true,
            CreationTime = Clock.Now,
        };
        await dbContext.ChainApiKeys.AddAsync(row);
        try
        {
            await dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_ColChainApiKeys_NetworkId_Provider_KeyHash") == true)
        {
            // 判重（AnyAsync）与插入之间的并发窗口：唯一索引兜底，转业务错误码
            throw new BusinessException(CollectionErrorCodes.ApiKeyExists).WithData("Provider", provider);
        }

        await uow.CompleteAsync();

        return ToApiKeyDto(row, masterKey);
    }

    /// <summary>删除一把 KEY（网关下一轮组装时生效；删到池空回退旧配置）。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task RemoveApiKeyAsync(Guid id, Guid? networkId = null)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await dbContext.ChainApiKeys.Where(k => k.Id == id && (!networkId.HasValue || k.NetworkId == networkId))
            .ExecuteDeleteAsync();
        await uow.CompleteAsync();
    }

    /// <summary>启停一把 KEY（停用即退出轮换，不删除）。</summary>
    [Authorize(CollectionPermissions.Network.Admin)]
    public virtual async Task SetApiKeyEnabledAsync(Guid id, bool isEnabled, Guid? networkId = null)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await dbContext.ChainApiKeys
            .Where(k => k.Id == id && (!networkId.HasValue || k.NetworkId == networkId))
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.IsEnabled, isEnabled));
        await uow.CompleteAsync();
    }

    // ── 内部 ─────────────────────────────────────────────────────────────────────────────

    /// <summary>链网运维仅 Host。</summary>
    private async Task CheckOperationsReadAccessAsync()
    {
        AssertHost();
        if (!await AuthorizationService.IsGrantedAsync(CollectionPermissions.Collections.Default)
            && !await AuthorizationService.IsGrantedAsync(CollectionPermissions.Network.Admin))
        {
            throw new AbpAuthorizationException();
        }
    }

    private void AssertHost()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }

    /// <summary>RPC 掩码仅显示协议、主机和端口，用户名、密码、路径和查询均隐藏。</summary>
    private async Task<string?> MaskUrlAsync(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try
        {
            var url = _encryptor.Decrypt(cipher.StartsWith('\u0001') ? cipher[1..] : cipher, await RequireMasterKeyAsync());
            if (url == null) throw new InvalidOperationException("decrypt returned null");
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return "***";
            }
            var endpoint = new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port);
            return endpoint.Uri.GetLeftPart(UriPartial.Authority) + "/***";
        }
        catch
        {
            return "***";
        }
    }

    private async Task<ColNetwork> RequireNetworkAsync(CollectionDbContext dbContext, Guid? networkId = null)
    {
        return await dbContext.Networks.Where(n => !networkId.HasValue || n.Id == networkId)
                   .OrderBy(n => n.CreationTime).FirstOrDefaultAsync()
               ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
    }

    private static async Task<ColNetwork> RequireAnkrNetworkAsync(CollectionDbContext dbContext, Guid? networkId)
    {
        return await dbContext.Networks.SingleOrDefaultAsync(network => network.Name == CollectionConsts.BscNetworkName
                   && (!networkId.HasValue || network.Id == networkId))
               ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
    }

    private async Task<string?> ReadProviderKeyAsync(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher))
        {
            return null;
        }

        try
        {
            var masterKey = await _chainKeys.GetAsync();
            return masterKey == null ? null : HdKeyService.UnprotectPlain(cipher, masterKey, _encryptor);
        }
        catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or FormatException)
        {
            return null;
        }
    }

    private async Task<ColToken> RequireTokenAsync(CollectionDbContext dbContext, string symbol, Guid? networkId = null)
    {
        var network = await RequireNetworkAsync(dbContext, networkId);
        return await dbContext.Tokens.FirstOrDefaultAsync(t => t.NetworkId == network.Id && t.Symbol == symbol)
               ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen).WithData("Symbol", symbol);
    }

    private async Task<string> RequireMasterKeyAsync()
    {
        return await _chainKeys.GetAsync()
               ?? throw new BusinessException(CollectionErrorCodes.MasterKeyMissing);
    }

    // ── KEY 池内部 ───────────────────────────────────────────────────────────────────────

    /// <summary>key 形状校验：Ankr 项目密钥 64 位十六进制；Etherscan API Key 34 位字母数字。</summary>
    private static bool IsValidKeyShape(ChainApiKeyProvider provider, string key)
    {
        return provider switch
        {
            ChainApiKeyProvider.RpcBsc => Regex.IsMatch(key, "^[0-9a-fA-F]{64}$"),
            ChainApiKeyProvider.EtherscanV2 => Regex.IsMatch(key, "^[A-Za-z0-9]{34}$"),
            _ => false,
        };
    }

    private static bool IsValidPlatformKey(string? key) =>
        key == null || (key.Length <= 2048 && Regex.IsMatch(key, @"\A[A-Za-z0-9._~+/-]+=*\z"));

    private static bool IsValidAnkrGroup(string? group) =>
        group == null || (group.Length <= 128 && !group.Any(char.IsWhiteSpace));

    /// <summary>掩码回显：**** + 尾 4 位（解密在内存完成，明文不返回不留日志）。</summary>
    private ChainApiKeyDto ToApiKeyDto(ColChainApiKey k, string masterKey)
    {
        string masked;
        try
        {
            var plain = _encryptor.Decrypt(
                k.KeyCipher.StartsWith('\u0001') ? k.KeyCipher[1..] : k.KeyCipher, masterKey);
            masked = MaskApiKey(plain);
        }
        catch
        {
            masked = "****"; // 口令轮换导致解不开：只给掩码，不让接口 500
        }

        return new ChainApiKeyDto
        {
            Id = k.Id,
            Provider = k.Provider.ToString(),
            Label = k.Label,
            MaskedKey = masked,
            IsEnabled = k.IsEnabled,
            LastUsedAt = k.LastUsedAt,
            ConsecutiveFailures = k.ConsecutiveFailures,
            CreationTime = k.CreationTime,
        };
    }

    private static string MaskApiKey(string? plain) => plain is { Length: > 4 } ? "****" + plain[^4..] : "****";
}
