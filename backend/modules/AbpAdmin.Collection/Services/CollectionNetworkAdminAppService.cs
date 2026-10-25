using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

/// <summary>链网运行状态（含代币通道进度）。</summary>
public class NetworkStatusDto
{
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

    /// <summary>大额门持有中进款单数（Confirmed 且未挂归集任务；等管理端放行的敞口规模）。</summary>
    public int HeldDepositCount { get; set; }

    /// <summary>持有中进款毛额合计（十进制字符串）。</summary>
    public string HeldDepositTotal { get; set; } = default!;

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
/// 链网管理接口（计划第 5.6/5.7 节的运维面，Host 专用）：
/// 配置 RPC（密文入库、明文只传不存）、一次性生成助记词（只展示一次）、回填测量上限、
/// 设金库地址/最低充值额。权限独立（Collection.Network.Admin）——链上配置是高危运维动作。
/// 扫链/归集开关与节奏在设置页（Collection.Chain.*，SettingUi 热改免重启）。
/// </summary>
[Authorize(AbpAdmin.Collection.Permissions.CollectionPermissions.Network.Admin)]
public class CollectionNetworkAdminAppService : ApplicationService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly IStringEncryptionService _encryptor;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly IAbpDistributedLock _distributedLock;

    public CollectionNetworkAdminAppService(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        IStringEncryptionService encryptor,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IAbpDistributedLock distributedLock)
    {
        _dbContextProvider = dbContextProvider;
        _encryptor = encryptor;
        _configuration = configuration;
        _distributedLock = distributedLock;
    }

    // ── 查询 ─────────────────────────────────────────────────────────────────────────────

    /// <summary>链网运行状态（含代币通道进度/告警冷却/开关状态）。</summary>
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
            .Where(o => o.NetworkId == network.Id && o.Status == DepositOrderStatus.Confirmed && o.SweepTaskId == null)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(o => o.Amount ?? 0m) })
            .FirstOrDefaultAsync();
        await uow.CompleteAsync();

        return new NetworkStatusDto
        {
            HeldDepositCount = held?.Count ?? 0,
            HeldDepositTotal = CollectionAmounts.Format(held?.Total ?? 0m),
            Network = network.Name,
            ChainId = network.ChainId,
            HasMnemonic = !string.IsNullOrEmpty(network.MnemonicCipher),
            RpcUrlMasked = MaskUrl(network.RpcCipher),
            NodeLogResultCap = network.NodeLogResultCap,
            LatestChainHead = network.LatestChainHead,
            ScanHeartbeatAt = network.ScanHeartbeatAt,
            SweepHeartbeatAt = network.SweepHeartbeatAt,
            ScanBehindAlertedAt = network.ScanBehindAlertedAt,
            SweepStuckAlertedAt = network.SweepStuckAlertedAt,
            GasLowAlertedAt = network.GasLowAlertedAt,
            ScanEnabled = await SettingProvider.IsTrueAsync(CollectionSettings.Chain.ScanEnabled),
            SweepEnabled = await SettingProvider.IsTrueAsync(CollectionSettings.Chain.SweepEnabled),
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

    /// <summary>进款单分页（管理端，可按状态/符号筛选）。</summary>
    /// <summary>
    /// 大额进款放行（只入队，不直接入账——入账仍走归集确认的净额路径，对齐线上 confirmDeposit）。
    /// 目标单：Confirmed 且未挂归集任务（大额门停下的单；普通漏挂单同样可放，幂等）。
    /// 链接写入用 CAS（WHERE SweepTaskId IS NULL）：并发双击/与扫链轮竞争时输家 0 行即返回，
    /// 不再各自建任务留下永孤 Queued（OCR：竞态窗口实存，靠地址互斥索引兜底只是不双扫）。
    /// </summary>
    public virtual async Task ApproveLargeDepositAsync(Guid id)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var order = await dbContext.DepositOrders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (order == null)
        {
            throw new BusinessException(CollectionErrorCodes.CollectionNotFound).WithData("DepositOrderId", id);
        }

        if (order.Status != DepositOrderStatus.Confirmed || order.SweepTaskId != null)
        {
            throw new BusinessException(CollectionErrorCodes.DepositNotReleasable)
                .WithData("Reason", $"仅 Confirmed 且未挂归集任务的单可放行（当前 {order.Status}，任务 {order.SweepTaskId?.ToString() ?? "无"}）");
        }

        // 复用确认路径的挂队语义：同址已有 Queued 任务则并入，否则新建
        var task = await dbContext.SweepTasks
            .Where(t => t.TokenId == order.TokenId && t.Status == SweepTaskStatus.Queued && t.AddressId == order.AddressId)
            .OrderBy(t => t.CreationTime).FirstOrDefaultAsync();
        if (task == null)
        {
            var address = await dbContext.DepositAddresses.AsNoTracking()
                .FirstAsync(a => a.Id == order.AddressId);
            task = new ColSweepTask(GuidGenerator.Create())
            {
                NetworkId = order.NetworkId,
                TokenId = order.TokenId,
                UserId = order.UserId,
                AddressId = order.AddressId,
                Address = order.ToAddress,
                AddressIndex = address.AddressIndex,
                Amount = 0m, // 激活时按已链接进款单求和
                Status = SweepTaskStatus.Queued,
                StatusChangedAt = Clock.Now,
                CreationTime = Clock.Now,
            };
            dbContext.SweepTasks.Add(task);
            await dbContext.SaveChangesAsync(); // 先落任务拿 Id，CAS 链接才能引用
        }

        // CAS 链接：并发放行/扫链挂队只有一个赢家；输家 0 行＝已被并发处置，幂等返回
        var linked = await dbContext.DepositOrders
            .Where(o => o.Id == id && o.Status == DepositOrderStatus.Confirmed && o.SweepTaskId == null)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.SweepTaskId, task.Id));
        if (linked == 0)
        {
            Logger.LogWarning("进款单 {OrderId} 放行链接被并发处置（0 行），幂等返回", id);
        }

        await uow.CompleteAsync();
    }

    public virtual async Task<PagedResultDto<DepositOrderListDto>> GetDepositOrdersAsync(
        int skipCount, int maxResultCount, string? status = null, string? symbol = null)
    {
        AssertHost();
        if (maxResultCount <= 0 || maxResultCount > 200) maxResultCount = 20;
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var symbols = await dbContext.Tokens.ToDictionaryAsync(t => t.Id, t => t.Symbol);
        var networkNames = await dbContext.Networks.ToDictionaryAsync(n => n.Id, n => n.Name);
        var query = dbContext.DepositOrders.AsNoTracking();

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
            CreationTime = o.CreationTime,
        }).ToList());
    }

    /// <summary>归集任务分页。</summary>
    public virtual async Task<PagedResultDto<SweepTaskListDto>> GetSweepTasksAsync(
        int skipCount, int maxResultCount, string? status = null)
    {
        AssertHost();
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
        AssertHost();
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

    /// <summary>配置 RPC 连接串（含项目密钥；加密入库，明文不落库不留日志）。</summary>
    [DisableAuditing]
    public virtual async Task SetRpcUrlAsync(string rpcUrl)
    {
        AssertHost();
        if (string.IsNullOrWhiteSpace(rpcUrl) || !rpcUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidRpcUrl).WithData("RpcUrl", rpcUrl);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireNetworkAsync(dbContext);
        var masterKey = RequireMasterKey();
        network.RpcCipher = '\u0001' + _encryptor.Encrypt(rpcUrl, masterKey);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>一次性生成助记词（只展示一次；已有地址或未完成归集则拒绝）。</summary>
    [DisableAuditing]
    public virtual async Task<string> GenerateMnemonicAsync()
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
        var network = await RequireNetworkAsync(dbContext);
        var masterKey = RequireMasterKey();

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

    /// <summary>回填测量上限（计划第 5.6 节：实测或文档，没有就不开扫链）。</summary>
    public virtual async Task SetNodeLogResultCapAsync(int cap)
    {
        AssertHost();
        if (cap < 100)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidCap).WithData("Cap", cap);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireNetworkAsync(dbContext);
        network.NodeLogResultCap = cap;
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>设置金库地址（需 0x + 40 位十六进制；fail-closed）。</summary>
    public virtual async Task SetVaultAddressAsync(string symbol, string vaultAddress)
    {
        AssertHost();
        if (string.IsNullOrWhiteSpace(vaultAddress) || !Regex.IsMatch(vaultAddress, "^0x[0-9a-fA-F]{40}$"))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidVaultAddress).WithData("VaultAddress", vaultAddress);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var token = await RequireTokenAsync(dbContext, symbol);
        token.VaultAddress = vaultAddress;
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>
    /// 设置充值策略：手续费率 DepositFeeRate（钳制 [0,1]）与自动归集额度 AutoCreditMaxAmount
    /// （钳制 ≥0，0=全部等放行）。建单时快照——改费率只影响新单，符合快照纪律。
    /// （OCR design：此前只有种子能写这两列，事故时无法经管理面降门/抬门。）
    /// </summary>
    public virtual async Task SetDepositPolicyAsync(decimal feeRate, decimal autoCreditMaxAmount)
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
        var network = await RequireNetworkAsync(dbContext);
        network.DepositFeeRate = feeRate;
        network.AutoCreditMaxAmount = autoCreditMaxAmount;
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>设置最低充值额（正数；负数/零会让粉尘过滤失效）。</summary>
    public virtual async Task SetMinDepositAsync(string symbol, string minDeposit)
    {
        AssertHost();
        var parsed = CollectionAmounts.Parse(minDeposit);
        if (parsed <= 0)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("MinDeposit", minDeposit);
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var token = await RequireTokenAsync(dbContext, symbol);
        token.MinDeposit = minDeposit;
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    /// <summary>切换代币通道启用（管理端控制某代币是否参与扫链/归集）。</summary>
    public virtual async Task SetTokenEnabledAsync(string symbol, bool isEnabled)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var token = await RequireTokenAsync(dbContext, symbol);
        token.IsEnabled = isEnabled;
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    // ── 扫链 KEY 池（同服务商多把，网关遇限流/认证错误自动轮换）──────────────────────────

    /// <summary>KEY 池全量（掩码回显，明文不出库；附回退来源可见度）。</summary>
    public virtual async Task<ChainApiKeysDto> GetApiKeysAsync()
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await RequireNetworkAsync(dbContext);
        var keys = await dbContext.ChainApiKeys.AsNoTracking()
            .Where(k => k.NetworkId == network.Id)
            .OrderBy(k => k.Provider).ThenBy(k => k.CreationTime)
            .ToListAsync();
        await uow.CompleteAsync();

        var masterKey = RequireMasterKey();
        return new ChainApiKeysDto
        {
            RpcKeys = keys.Where(k => k.Provider == ChainApiKeyProvider.RpcBsc)
                .Select(k => ToApiKeyDto(k, masterKey)).ToList(),
            EtherscanKeys = keys.Where(k => k.Provider == ChainApiKeyProvider.EtherscanV2)
                .Select(k => ToApiKeyDto(k, masterKey)).ToList(),
            RpcFallbackMasked = MaskUrl(network.RpcCipher),
            EtherscanFallbackPresent = !string.IsNullOrEmpty(
                _configuration["Collection:Chain:EtherscanV2Endpoint"]),
        };
    }

    /// <summary>添加一把扫链 KEY（明文只传不存；同服务商按 SHA-256 判重；审计不留明文）。</summary>
    [DisableAuditing]
    public virtual async Task<ChainApiKeyDto> AddApiKeyAsync(string provider, string key, string? label)
    {
        AssertHost();
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
        var network = await RequireNetworkAsync(dbContext);

        var keyHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(trimmed))).ToLowerInvariant();
        var duplicate = await dbContext.ChainApiKeys.AnyAsync(k =>
            k.NetworkId == network.Id && k.Provider == providerType && k.KeyHash == keyHash);
        if (duplicate)
        {
            throw new BusinessException(CollectionErrorCodes.ApiKeyExists).WithData("Provider", provider);
        }

        var masterKey = RequireMasterKey();
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
    public virtual async Task RemoveApiKeyAsync(Guid id)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await dbContext.ChainApiKeys.Where(k => k.Id == id).ExecuteDeleteAsync();
        await uow.CompleteAsync();
    }

    /// <summary>启停一把 KEY（停用即退出轮换，不删除）。</summary>
    public virtual async Task SetApiKeyEnabledAsync(Guid id, bool isEnabled)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await dbContext.ChainApiKeys
            .Where(k => k.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(k => k.IsEnabled, isEnabled));
        await uow.CompleteAsync();
    }

    // ── 内部 ─────────────────────────────────────────────────────────────────────────────

    /// <summary>链网运维仅 Host。</summary>
    private void AssertHost()
    {
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }

    /// <summary>RPC 掩码：显示协议+域名+路径前缀，密钥部分用 ***。</summary>
    private string? MaskUrl(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try
        {
            var url = _encryptor.Decrypt(cipher.StartsWith('\u0001') ? cipher[1..] : cipher, RequireMasterKey());
            if (url == null) throw new InvalidOperationException("decrypt returned null");
            var idx = url.IndexOf('/', "https://".Length);
            return idx < 0 ? url : url[..idx] + "/***";
        }
        catch
        {
            return "***";
        }
    }

    private async Task<ColNetwork> RequireNetworkAsync(CollectionDbContext dbContext)
    {
        return await dbContext.Networks.OrderBy(n => n.CreationTime).FirstOrDefaultAsync()
               ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);
    }

    private async Task<ColToken> RequireTokenAsync(CollectionDbContext dbContext, string symbol)
    {
        return await dbContext.Tokens.FirstOrDefaultAsync(t => t.Symbol == symbol)
               ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen).WithData("Symbol", symbol);
    }

    private string RequireMasterKey()
    {
        return _configuration["Collection:MasterKey"]
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

    /// <summary>掩码回显：**** + 尾 4 位（解密在内存完成，明文不返回不留日志）。</summary>
    private ChainApiKeyDto ToApiKeyDto(ColChainApiKey k, string masterKey)
    {
        string masked;
        try
        {
            var plain = _encryptor.Decrypt(
                k.KeyCipher.StartsWith('\u0001') ? k.KeyCipher[1..] : k.KeyCipher, masterKey);
            plain ??= string.Empty;
            masked = plain.Length <= 4 ? "****" : "****" + plain[^4..];
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
}
