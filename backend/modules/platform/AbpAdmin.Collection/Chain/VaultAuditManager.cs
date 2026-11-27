using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
using Microsoft.Extensions.Logging;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 按币留存金库确认块、余额快照和已核实的归集记账时差。缺口与证据不足分别告警。
/// </summary>
public class VaultAuditManager : DomainService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ChainAlertService _alerts;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly Volo.Abp.Guids.IGuidGenerator _guidGenerator;
    private readonly Contracts.IChainPushNotifier? _pushNotifier;
    private readonly ISettingProvider _settings;

    public VaultAuditManager(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ChainAlertService alerts,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        Volo.Abp.Guids.IGuidGenerator guidGenerator,
        ISettingProvider settings,
        System.IServiceProvider? serviceProvider = null)
    {
        _dbContextProvider = dbContextProvider;
        _alerts = alerts;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
        _guidGenerator = guidGenerator;
        _settings = settings;
        _pushNotifier = serviceProvider?.GetService(typeof(Contracts.IChainPushNotifier)) as Contracts.IChainPushNotifier;
    }

    public virtual async Task RunAsync(ColNetwork network, ColToken token, IBscChainGateway gateway, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(token.VaultAddress))
        {
            Logger.LogWarning("代币 {Symbol} 未配置金库地址，金库核对跳过", token.Symbol);
            return;
        }

        var currency = token.GetBalanceCurrency();
        var confirmations = await _settings.GetIntAsync(CollectionSettings.Chain.Confirmations, ChainDefaults.Confirmations);
        long referenceBlock;
        string rawVault;
        try
        {
            var head = await gateway.GetLatestBlockNumberAsync(ct);
            if (confirmations < 0 || head < confirmations)
                throw new ChainRpcException("未取得达到确认深度的区块。");
            referenceBlock = head - confirmations;
            rawVault = await gateway.GetTokenBalanceRawAtBlockAsync(token.ContractAddress, token.VaultAddress, referenceBlock, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            Logger.LogWarning(ex, "无法取得代币 {Symbol} 的确认块金库余额", token.Symbol);
            await _alerts.RaiseAsync(null, "金库核对未取得链上余额",
                $"代币 {token.Symbol} 本次无法读取确认块或金库余额，最近一次核对记录仍为历史结果。");
            throw;
        }

        var vaultBalance = DepositScanManager.TryScaleRaw(rawVault, token.Decimals, out var overflow);
        if (overflow)
        {
            await _alerts.RaiseAsync(null, "金库余额超出精度",
                $"代币 {token.Symbol} 在确认块 {referenceBlock} 的金库余额原值 {rawVault} 无法精确存入 numeric(28,18)，本次未生成核对记录。");
            return;
        }

        VaultAuditEvidence evidence;
        using (var snapshot = _unitOfWorkManager.Begin(new AbpUnitOfWorkOptions
        {
            IsTransactional = true, IsolationLevel = System.Data.IsolationLevel.RepeatableRead,
        }, requiresNew: true))
        {
            evidence = await VaultAuditEvidence.ReadAsync(await _dbContextProvider.GetDbContextAsync(),
                network, token, currency, referenceBlock, ct);
            await snapshot.CompleteAsync(ct);
        }
        if (evidence.UserTotal == VaultAuditEvidence.StorageLimit)
        {
            await _alerts.RaiseAsync(null, "金库核对用户余额合计超出精度",
                $"代币 {token.Symbol} 可用与冻结余额合计无法写入 numeric(28,18)，请人工核对。");
            return;
        }

        var (pending, after) = await evidence.CompareAsync(gateway, token, referenceBlock, confirmations, Logger, ct);
        var difference = vaultBalance!.Value - evidence.UserTotal;
        var reconciled = difference + after - pending;
        if (reconciled is <= -VaultAuditEvidence.StorageLimit or >= VaultAuditEvidence.StorageLimit) reconciled = null;
        var status = reconciled switch
        {
            null => VaultReconciliationStatus.Inconclusive,
            < 0 => VaultReconciliationStatus.Shortfall,
            _ when pending > 0 || after > 0 => VaultReconciliationStatus.TimingDifference,
            > 0 => VaultReconciliationStatus.Surplus,
            _ => VaultReconciliationStatus.Balanced,
        };
        var check = new ColVaultCheck(_guidGenerator.Create())
        {
            NetworkId = network.Id,
            TokenId = token.Id,
            Symbol = token.Symbol,
            RawVaultAmount = rawVault,
            VaultBalance = vaultBalance.Value,
            UserTotal = evidence.UserTotal,
            Difference = difference,
            ReferenceBlock = referenceBlock,
            ReferenceVaultAddress = token.VaultAddress,
            PendingCredit = pending,
            PostedAfterBlock = after,
            ReconciledDifference = reconciled,
            ReconciliationStatus = status,
            CheckedAt = _clock.Now,
        };
        using (var write = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true))
        {
            (await _dbContextProvider.GetDbContextAsync()).VaultChecks.Add(check);
            await write.CompleteAsync(ct);
        }
        if (status == VaultReconciliationStatus.Shortfall)
            await _alerts.RaiseAsync(null, "金库核对确认缺口",
                $"代币 {token.Symbol} 在确认块 {referenceBlock} 核对记账时差后仍有缺口 {CollectionAmounts.Format(-reconciled!.Value)}。核对记录 {check.Id}。");
        else if (status == VaultReconciliationStatus.Inconclusive)
            await _alerts.RaiseAsync(null, "金库核对证据不足",
                $"代币 {token.Symbol} 在确认块 {referenceBlock} 的原始差额为 {CollectionAmounts.Format(difference)}，归集、同地址提现或金额尚待核实，请核查记录 {check.Id}。");
        Logger.LogInformation("金库核对 {CheckId} {Symbol}：block={Block} status={Status}", check.Id, token.Symbol, referenceBlock, status);

        if (_pushNotifier != null)
        {
            try
            {
                await _pushNotifier.PushVaultCheckAsync(new Contracts.ChainVaultCheckEto
                {
                    CheckId = check.Id, Symbol = check.Symbol,
                    VaultBalance = CollectionAmounts.Format(check.VaultBalance),
                    UserTotal = CollectionAmounts.Format(check.UserTotal),
                    Difference = CollectionAmounts.Format(check.Difference),
                    ReconciliationStatus = status.ToString(),
                    ReferenceBlock = referenceBlock,
                    ReferenceVaultAddress = check.ReferenceVaultAddress,
                    PendingCredit = CollectionAmounts.FormatNullable(pending),
                    PostedAfterBlock = CollectionAmounts.FormatNullable(after),
                    ReconciledDifference = CollectionAmounts.FormatNullable(reconciled),
                    CheckedAt = check.CheckedAt,
                });
            }
            catch (Exception exception)
            {
                Logger.LogWarning(exception, "金库核对 {CheckId} 已保存，实时通知发送失败", check.Id);
            }
        }
    }
}
