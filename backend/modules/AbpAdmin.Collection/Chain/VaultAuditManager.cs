using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 金库核对（计划第 5.6 节）：每日按币 balanceOf(金库) vs 用户余额合计，追加 ColVaultChecks；
/// 金库 &lt; 用户余额则告警。未入账的在途（Confirmed 未 Swept）不加入右边——归集完成前加入会误报。
/// 正差额不告警（多出来的粉尘/礼品属金库盈余）。
/// </summary>
public class VaultAuditManager : DomainService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ChainAlertService _alerts;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly Volo.Abp.Guids.IGuidGenerator _guidGenerator;
    private readonly Contracts.IChainPushNotifier? _pushNotifier;

    public VaultAuditManager(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ChainAlertService alerts,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        Volo.Abp.Guids.IGuidGenerator guidGenerator,
        System.IServiceProvider? serviceProvider = null)
    {
        _dbContextProvider = dbContextProvider;
        _alerts = alerts;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
        _guidGenerator = guidGenerator;
        _pushNotifier = serviceProvider?.GetService(typeof(Contracts.IChainPushNotifier)) as Contracts.IChainPushNotifier;
    }

    public virtual async Task RunAsync(ColNetwork network, ColToken token, IBscChainGateway gateway, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token.VaultAddress))
        {
            Logger.LogWarning("代币 {Symbol} 未配置金库地址，金库核对跳过", token.Symbol);
            return;
        }

        // 链上读先行（外部 I/O 不开事务）；事务只包 SUM 读 + 留痕写
        var rawVault = await gateway.GetTokenBalanceRawAsync(token.ContractAddress, token.VaultAddress!, ct);

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;

        var vaultBalance = DepositScanManager.TryScaleRaw(rawVault, token.Decimals, out var overflow);
        if (overflow)
        {
            // 金库余额放不进 decimal 列本身就是重大异常：告警并以原始值留痕不了账
            await _alerts.RaiseAsync(null, "金库余额超出精度",
                $"代币 {token.Symbol} 金库 balanceOf={rawVault} 放不进 numeric(28,18)，请人工核对。");
            await uow.CompleteAsync();
            return;
        }

        var currency = MapCurrency(token.Symbol);
        var userTotal = await dbContext.Balances
            .Where(b => b.Currency == currency)
            .SumAsync(b => b.Balance, ct);

        dbContext.VaultChecks.Add(new ColVaultCheck(_guidGenerator.Create())
        {
            NetworkId = network.Id,
            TokenId = token.Id,
            Symbol = token.Symbol,
            RawVaultAmount = rawVault,
            VaultBalance = vaultBalance!.Value,
            UserTotal = userTotal,
            Difference = vaultBalance.Value - userTotal,
            CheckedAt = now,
        });

        if (vaultBalance < userTotal)
        {
            await _alerts.RaiseAsync(null, "金库余额小于用户余额合计",
                $"代币 {token.Symbol} 金库 {vaultBalance:0.######} < 用户余额合计 {userTotal:0.######}，缺口 {userTotal - vaultBalance:0.######}。未入账在途不计入右侧（计划口径）。");
        }

        await dbContext.SaveChangesAsync(ct);
        await uow.CompleteAsync();
        Logger.LogInformation("金库核对 {Symbol}：vault={Vault} users={Users}", token.Symbol, vaultBalance, userTotal);

        if (_pushNotifier != null)
        {
            try
            {
                await _pushNotifier.PushVaultCheckAsync(new Contracts.ChainVaultCheckEto
                {
                    CheckId = Guid.NewGuid(), Symbol = token.Symbol,
                    VaultBalance = Contracts.CollectionAmounts.Format(vaultBalance.Value),
                    UserTotal = Contracts.CollectionAmounts.Format(userTotal),
                    Difference = Contracts.CollectionAmounts.Format(vaultBalance.Value - userTotal),
                    CheckedAt = now,
                });
            }
            catch
            {
                // 推送失败不影响核对
            }
        }
    }

    private static CollectionCurrency MapCurrency(string symbol)
    {
        return symbol.Equals("USDC", StringComparison.OrdinalIgnoreCase)
            ? CollectionCurrency.Usdc
            : CollectionCurrency.Usdt;
    }
}
