using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Entities;
using AbpAdmin.ScheduledJobs;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Jobs;

/// <summary>
/// 链网种子（计划第 5.6 节表清单）：BSC 网络行 + USDT/USDC 代币行（合约为主网公认地址，
/// MinDeposit 按运行说明上调，先给保守值）+ 金库核对作业（Host 专用、默认停用，同两模块种子纪律）。
/// 密文（RPC/助记词）不进种子——由运维经管理接口写入。NodeLogResultCap 留空 = 扫链不可开（无缺省）。
/// </summary>
public class CollectionChainSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<ColNetwork, Guid> _networkRepository;
    private readonly IRepository<ColToken, Guid> _tokenRepository;
    private readonly IRepository<ScheduledJob, Guid> _jobRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;

    public CollectionChainSeedContributor(
        IRepository<ColNetwork, Guid> networkRepository,
        IRepository<ColToken, Guid> tokenRepository,
        IRepository<ScheduledJob, Guid> jobRepository,
        IGuidGenerator guidGenerator,
        IClock clock)
    {
        _networkRepository = networkRepository;
        _tokenRepository = tokenRepository;
        _jobRepository = jobRepository;
        _guidGenerator = guidGenerator;
        _clock = clock;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId != null)
        {
            return;
        }

        var network = await _networkRepository.FirstOrDefaultAsync(n => n.Name == "BSC");
        if (network == null)
        {
            network = new ColNetwork(_guidGenerator.Create())
            {
                Name = "BSC",
                ChainId = 56,
                CreationTime = _clock.Now,
            };
            await _networkRepository.InsertAsync(network, autoSave: true);
        }

        foreach (var (symbol, contract, minDeposit) in new[]
                 {
                     ("USDT", "0x55d398326f99059fF775485246999027B3197955", "10"),
                     ("USDC", "0x8AC76a51cc950d9822D68b83fE1Ad97B32Cd580d", "10"),
                 })
        {
            if (!await _tokenRepository.AnyAsync(t => t.NetworkId == network.Id && t.Symbol == symbol))
            {
                await _tokenRepository.InsertAsync(new ColToken(_guidGenerator.Create())
                {
                    NetworkId = network.Id,
                    Symbol = symbol,
                    ContractAddress = contract,
                    Decimals = 18,
                    MinDeposit = minDeposit,
                    IsEnabled = true,
                    CreationTime = _clock.Now,
                }, autoSave: true);
            }
        }

        var jobTypes = (await _jobRepository.GetListAsync()).Select(x => x.JobType).ToHashSet();
        if (!jobTypes.Contains(ScheduledJobConsts.VaultAuditJobType))
        {
            await _jobRepository.InsertAsync(new ScheduledJob(
                _guidGenerator.Create(),
                context.TenantId,
                ScheduledJobConsts.VaultAuditJobType,
                jobType: ScheduledJobConsts.VaultAuditJobType,
                cronExpression: "0 40 4 * * ?",
                description: "金库核对：按币 balanceOf(金库) vs 用户余额合计，追加 ColVaultChecks，负差额告警（每日）",
                isEnabled: false));
        }
    }
}
