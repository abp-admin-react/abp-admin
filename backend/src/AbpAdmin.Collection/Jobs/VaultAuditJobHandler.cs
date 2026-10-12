using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.ScheduledJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Jobs;

/// <summary>
/// 金库核对定时作业（计划第 57、291 行）：每日按币 balanceOf(金库) vs 用户余额合计，
/// 追加 ColVaultChecks；负差额告警（固定前缀之外的普通告警）。Host 专用、种子默认停用。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler))]
public class VaultAuditJobHandler : IScheduledJobHandler, ITransientDependency
{
    public string JobType => ScheduledJobConsts.VaultAuditJobType;

    public string DisplayNameKey => "ScheduledJobType:VaultAudit";

    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly VaultAuditManager _vaultAuditManager;
    private readonly ChainGatewayFactory _gatewayFactory;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ILogger<VaultAuditJobHandler> _logger;

    public VaultAuditJobHandler(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        VaultAuditManager vaultAuditManager,
        ChainGatewayFactory gatewayFactory,
        IUnitOfWorkManager unitOfWorkManager,
        ILogger<VaultAuditJobHandler> logger)
    {
        _dbContextProvider = dbContextProvider;
        _vaultAuditManager = vaultAuditManager;
        _gatewayFactory = gatewayFactory;
        _unitOfWorkManager = unitOfWorkManager;
        _logger = logger;
    }

    [UnitOfWork(isTransactional: false)]
    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks.OrderBy(n => n.CreationTime).FirstOrDefaultAsync();
        var hasRpc = network != null && (
            await dbContext.ChainApiKeys.AnyAsync(
                k => k.NetworkId == network.Id
                     && k.Provider == ChainApiKeyProvider.RpcBsc && k.IsEnabled)
            || !string.IsNullOrEmpty(network.RpcCipher));
        if (network?.NodeLogResultCap is not int cap || !hasRpc)
        {
            _logger.LogWarning("金库核对跳过：网络未配置扫链 key（池/RpcCipher 均空）或测量上限（{Network}）",
                network?.Name ?? "(无网络行)");
            await uow.CompleteAsync();
            return;
        }

        var tokens = await dbContext.Tokens
            .Where(t => t.NetworkId == network.Id && t.IsEnabled)
            .ToListAsync();

        // 网关在工作单元内组装（key 池查询/LastUsedAt 回写要活的上下文；池空回退 RpcCipher 在工厂内部）
        var gateway = await _gatewayFactory.CreateAsync(dbContext, network, cap);
        await uow.CompleteAsync();
        foreach (var token in tokens)
        {
            try
            {
                await _vaultAuditManager.RunAsync(network, token, gateway, context.CancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "金库核对 {Symbol} 失败，跳过该币", token.Symbol);
            }
        }
    }
}
