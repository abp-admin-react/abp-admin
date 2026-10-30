using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Settings;
using Volo.Abp.Timing;

namespace AbpAdmin.Collection.Withdrawals;

/// <summary>热钱包快照（审批与广播共用；私钥只在签名临界区内解出）。</summary>
public record WithdrawHotWallet(ColNetwork Network, string Address, string KeyCipher)
{
    /// <summary>在签名临界区内解私钥；用完即弃（字符串不可擦除，靠作用域收敛暴露面）。</summary>
    public string DecryptKey(string masterKey, IStringEncryptionService encryptor)
    {
        return HdKeyService.UnprotectPlain(KeyCipher, masterKey, encryptor)
               ?? throw new BusinessException(CollectionErrorCodes.HotWalletNotConfigured)
                   .WithData("Reason", "key cipher decrypt failed (master key rotated?)");
    }
}

/// <summary>
/// 提现护栏（M3）：热钱包偿付检查 + 熔断地板 + BNB 门槛。
/// 检查点三处——管理端/自动审批前、广播前、签名临界区内发送前（关 TOCTOU）。
/// 低于 USDT 地板 = 熔断：阻断所有审批（不只是本单）并告警，运维补币后自动恢复。
/// </summary>
public class WithdrawGuard : DomainService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ISettingProvider _settings;
    private readonly ChainAlertService _alerts;
    private readonly IStringEncryptionService _encryptor;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly IClock _clock;

    public WithdrawGuard(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ISettingProvider settings,
        ChainAlertService alerts,
        IStringEncryptionService encryptor,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IClock clock)
    {
        _dbContextProvider = dbContextProvider;
        _settings = settings;
        _alerts = alerts;
        _encryptor = encryptor;
        _configuration = configuration;
        _clock = clock;
    }

    /// <summary>载入热钱包（未配置返回 null——调用方按 HotWalletNotConfigured 拒绝）。</summary>
    public virtual async Task<WithdrawHotWallet?> LoadAsync(CancellationToken ct = default)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks.AsNoTracking()
            .OrderBy(n => n.CreationTime).FirstOrDefaultAsync(ct);
        if (network == null || string.IsNullOrEmpty(network.WithdrawVaultAddress)
            || string.IsNullOrEmpty(network.WithdrawVaultKeyCipher))
        {
            return null;
        }

        return new WithdrawHotWallet(network, network.WithdrawVaultAddress!, network.WithdrawVaultKeyCipher!);
    }

    /// <summary>
    /// 审批/广播前的偿付检查：热钱包代币余额 ≥ 本次净额、≥ 熔断地板、BNB ≥ 门槛。
    /// 任一不满足抛对应错误码（调用方拒绝该单）；熔断额外告警并阻断所有后续审批。
    /// </summary>
    public virtual async Task EnsureCanPayAsync(
        WithdrawHotWallet wallet, IBscChainGateway gateway, ColToken token,
        decimal actualAmount, CancellationToken ct = default)
    {
        // 熔断地板按 USDT 计（热钱包主币种；USDC 共用同一地板口径）
        var raw = await gateway.GetTokenBalanceRawAsync(token.ContractAddress, wallet.Address, ct);
        var balance = DepositScanManager.TryScaleRaw(raw, token.Decimals, out var overflow);
        if (overflow || balance == null)
        {
            throw new BusinessException(CollectionErrorCodes.HotWalletInsolvent)
                .WithData("Reason", "balance scale overflow");
        }

        var floor = await GetSettingDecimalAsync(CollectionSettings.Withdraw.HotWalletUsdtFloor, WithdrawDefaults.HotWalletUsdtFloor);
        if (balance < floor)
        {
            await _alerts.RaiseAsync(null, "提现热钱包熔断",
                $"热钱包 {wallet.Address} {token.Symbol} 余额 {balance:0.######} 低于熔断地板 {floor:0.######}，" +
                "所有提现审批已阻断，请补币或人工处理。");
            throw new BusinessException(CollectionErrorCodes.WithdrawBreakerOpen)
                .WithData("Balance", CollectionAmounts.Format(balance.Value))
                .WithData("Floor", CollectionAmounts.Format(floor));
        }

        if (balance < actualAmount)
        {
            throw new BusinessException(CollectionErrorCodes.HotWalletInsolvent)
                .WithData("Balance", CollectionAmounts.Format(balance.Value))
                .WithData("Amount", CollectionAmounts.Format(actualAmount));
        }

        var minBnb = await GetSettingDecimalAsync(CollectionSettings.Withdraw.MinHotWalletBnb, WithdrawDefaults.MinHotWalletBnb);
        var bnb = await gateway.GetBnbBalanceAsync(wallet.Address, ct);
        if (bnb < minBnb)
        {
            await _alerts.RaiseAsync(null, "提现热钱包 BNB 不足",
                $"热钱包 {wallet.Address} BNB {bnb:0.######} 低于门槛 {minBnb:0.######}（付不了 gas），提现审批已阻断。");
            throw new BusinessException(CollectionErrorCodes.HotWalletGasShort)
                .WithData("Bnb", bnb.ToString("0.######"))
                .WithData("Min", minBnb.ToString("0.######"));
        }
    }

    /// <summary>状态卡数据：热钱包余额/门槛/熔断状态（不暴露任何密钥信息）。</summary>
    public virtual async Task<WithdrawHotWalletStatusDto> GetStatusAsync(IBscChainGateway gateway, CancellationToken ct = default)
    {
        var wallet = await LoadAsync(ct);
        if (wallet == null)
        {
            return new WithdrawHotWalletStatusDto { Configured = false };
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var tokens = await dbContext.Tokens.AsNoTracking()
            .Where(t => t.NetworkId == wallet.Network.Id && t.IsEnabled).ToListAsync(ct);
        var floor = await GetSettingDecimalAsync(CollectionSettings.Withdraw.HotWalletUsdtFloor, WithdrawDefaults.HotWalletUsdtFloor);
        var minBnb = await GetSettingDecimalAsync(CollectionSettings.Withdraw.MinHotWalletBnb, WithdrawDefaults.MinHotWalletBnb);

        var dto = new WithdrawHotWalletStatusDto
        {
            Configured = true,
            Address = wallet.Address,
            UsdtFloor = CollectionAmounts.Format(floor),
            MinBnb = CollectionAmounts.Format(minBnb),
            Bnb = CollectionAmounts.Format(await gateway.GetBnbBalanceAsync(wallet.Address, ct)),
            // 熔断口径与 EnsureCanPayAsync 统一：任一启用币种余额低于地板即熔断（只看 USDT 一行会漏判，
            // USDT 被禁用时状态卡静默显示健康——与审批侧实际判定分裂）；无任何启用币种也视为熔断
            BreakerOpen = tokens.Count == 0,
        };
        foreach (var token in tokens)
        {
            var raw = await gateway.GetTokenBalanceRawAsync(token.ContractAddress, wallet.Address, ct);
            var scaled = DepositScanManager.TryScaleRaw(raw, token.Decimals, out var overflow);
            var value = overflow || scaled == null ? 0m : scaled.Value;
            dto.TokenBalances[token.Symbol] = CollectionAmounts.Format(value);
            if (value < floor)
            {
                dto.BreakerOpen = true;
            }
        }

        return dto;
    }

    /// <summary>加密热钱包私钥入密文（管理端 Set 用；与 HdKeyService 同一版本前缀协议）。</summary>
    public virtual string ProtectKey(string privateKey)
    {
        var masterKey = _configuration[CollectionSettings.Host.MasterKey]
                        ?? throw new BusinessException(CollectionErrorCodes.MasterKeyMissing);
        return HdKeyService.Protect(privateKey.Trim(), masterKey, _encryptor); // Protect 即通用敏感明文加密（原 ProtectPlain 与其逐字重复，已删）
    }

    private async Task<decimal> GetSettingDecimalAsync(string key, decimal fallback)
    {
        var raw = await _settings.GetOrNullAsync(key);
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v
            : fallback;
    }
}

/// <summary>热钱包状态卡（无密钥）。</summary>
public class WithdrawHotWalletStatusDto
{
    public bool Configured { get; set; }

    public string? Address { get; set; }

    public string UsdtFloor { get; set; } = default!;

    public string MinBnb { get; set; } = default!;

    public string Bnb { get; set; } = default!;

    public bool BreakerOpen { get; set; }

    public System.Collections.Generic.Dictionary<string, string> TokenBalances { get; set; } = new();
}
