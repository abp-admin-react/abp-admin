using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Encryption;
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
    public string RawAmount { get; set; } = default!;
    public string Symbol { get; set; } = default!;
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

/// <summary>
/// 链网管理接口（计划第 5.6/5.7 节的运维面，Host 专用）：
/// 配置 RPC（密文入库、明文只传不存）、一次性生成助记词（只展示一次）、回填测量上限、
/// 设金库地址/最低充值额。权限独立（Collection.Network.Admin）——链上配置是高危运维动作。
/// 扫链/归集开关也在此页面切换（写入 Collection:Chain 配置节，热生效）。
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
    public virtual async Task<NetworkStatusDto> GetStatusAsync()
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks.OrderBy(n => n.CreationTime).FirstOrDefaultAsync()
                      ?? throw new BusinessException(CollectionErrorCodes.DepositNotOpen);

        var tokens = await dbContext.Tokens
            .Where(t => t.NetworkId == network.Id)
            .OrderBy(t => t.Symbol)
            .ToListAsync();
        var head = network.LatestChainHead ?? 0;
        await uow.CompleteAsync();

        return new NetworkStatusDto
        {
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
            ScanEnabled = (_configuration.GetSection("Collection:Chain").GetValue("ScanEnabled", false)),
            SweepEnabled = (_configuration.GetSection("Collection:Chain").GetValue("SweepEnabled", false)),
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
    public virtual async Task<PagedResultDto<DepositOrderListDto>> GetDepositOrdersAsync(
        int skipCount, int maxResultCount, string? status = null, string? symbol = null)
    {
        AssertHost();
        if (maxResultCount <= 0 || maxResultCount > 200) maxResultCount = 20;
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var symbols = await dbContext.Tokens.ToDictionaryAsync(t => t.Id, t => t.Symbol);
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
            RawAmount = o.RawAmount,
            Symbol = symbols.TryGetValue(o.TokenId, out var s) ? s : "?",
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
}
