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
/// 核对 BSC 主网启用代币的金库余额。单个代币读取失败时继续处理其他代币。
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
        var network = await dbContext.FindBscMainnetAsync();
        if (network == null || (string.IsNullOrEmpty(network.RpcCipher)
            && !await dbContext.ChainApiKeys.AnyAsync(k => k.NetworkId == network.Id
                && k.Provider == ChainApiKeyProvider.RpcBsc && k.IsEnabled)))
        {
            _logger.LogWarning("金库核对跳过：网络未配置交易 RPC（{Network}）",
                network?.Name ?? "(无网络行)");
            await uow.CompleteAsync();
            return;
        }

        var tokens = await dbContext.Tokens
            .Where(t => t.NetworkId == network.Id && t.IsEnabled)
            .ToListAsync();

        // 网关在工作单元内组装（key 池查询/LastUsedAt 回写要活的上下文；池空回退 RpcCipher 在工厂内部）
        var gateway = await _gatewayFactory.CreateAsync(dbContext, network, ChainGatewayFactory.TransactionLogResultCap);
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
