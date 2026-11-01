using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
using AbpAdmin.ScheduledJobs;
using Microsoft.Extensions.Configuration;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Jobs;

/// <summary>
/// 链网种子（计划第 5.6 节表清单）：BSC 网络行 + USDT/USDC 代币行（合约为主网公认地址，
/// MinDeposit 按运行说明上调，先给保守值）+ 金库核对作业（Host 专用、默认停用，同两模块种子纪律）。
/// 密文（RPC/助记词）不进种子——由运维经管理接口写入。NodeLogResultCap 留空 = 扫链不可开（无缺省）。
/// 另含开关的一次性部署引导（Collection:Bootstrap:* → 设置库；见 SeedBootstrapSwitchesAsync）。
/// </summary>
public class CollectionChainSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly IRepository<ColNetwork, Guid> _networkRepository;
    private readonly IRepository<ColToken, Guid> _tokenRepository;
    private readonly IRepository<ScheduledJob, Guid> _jobRepository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;
    private readonly ISettingManager _settingManager;
    private readonly ISettingManagementStore _settingStore;

    public CollectionChainSeedContributor(
        IRepository<ColNetwork, Guid> networkRepository,
        IRepository<ColToken, Guid> tokenRepository,
        IRepository<ScheduledJob, Guid> jobRepository,
        IGuidGenerator guidGenerator,
        IClock clock,
        IConfiguration configuration,
        ISettingManager settingManager,
        ISettingManagementStore settingStore)
    {
        _networkRepository = networkRepository;
        _tokenRepository = tokenRepository;
        _jobRepository = jobRepository;
        _guidGenerator = guidGenerator;
        _clock = clock;
        _configuration = configuration;
        _settingManager = settingManager;
        _settingStore = settingStore;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId != null)
        {
            return;
        }

        await SeedBootstrapSwitchesAsync();

        var network = await _networkRepository.FirstOrDefaultAsync(n => n.Name == CollectionConsts.BscNetworkName);
        if (network == null)
        {
            network = new ColNetwork(_guidGenerator.Create())
            {
                Name = CollectionConsts.BscNetworkName,
                ChainId = 56,
                // 充值手续费/大额放行（对齐线上默认）：费率 0 = 不收费；自动归集额度 5000，
                // 0 是「全部等人放行」哨兵不是「不限制」——既有行更新时不覆盖（运维显式改）
                DepositFeeRate = 0m,
                AutoCreditMaxAmount = 5000m,
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

    /// <summary>
    /// 开关的一次性部署引导：Collection:Bootstrap:ScanEnabled 等配置为 true 时把对应设置（全局）写成开。
    /// 幂等口径：只写「从未显式设置过」的键（G 存储无值）——之后再跑种子不会覆盖设置页的管理动作
    /// （部署时开了、运营中在设置页关掉，重跑迁移不会又被翻开）。用途：首次部署/e2e 用环境变量打开
    /// 扫链/归集/提现，之后一律设置页热改。
    /// </summary>
    private async Task SeedBootstrapSwitchesAsync()
    {
        foreach (var (bootstrapKey, settingKey) in new[]
                 {
                     (CollectionSettings.Host.Bootstrap.ScanEnabled, CollectionSettings.Chain.ScanEnabled),
                     (CollectionSettings.Host.Bootstrap.SweepEnabled, CollectionSettings.Chain.SweepEnabled),
                     (CollectionSettings.Host.Bootstrap.WithdrawEnabled, CollectionSettings.Withdraw.Enabled),
                     (CollectionSettings.Host.Bootstrap.HotWalletWatchEnabled,
                         CollectionSettings.Chain.HotWalletWatchEnabled),
                 })
        {
            if (!_configuration.GetValue(bootstrapKey, false))
            {
                continue;
            }

            var stored = await _settingStore.GetOrNullAsync(settingKey, GlobalSettingValueProvider.ProviderName, null);
            if (stored == null)
            {
                await _settingManager.SetGlobalAsync(settingKey, "true");
            }
        }
    }
}
