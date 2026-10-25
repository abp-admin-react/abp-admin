using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 链上 worker 的宿主配置残留项（appsettings/secrets，冒号键）：密钥/端点/告警收件人与
/// 墙钟预算——都不热改（预算是挂死防护阀；密钥不进设置页）。
/// 开关/间隔/确认数/归集阈值等运行期参数已迁设置页（Collection.Chain.* / Collection.Withdraw.*，
/// SettingUi 热改，worker 每轮热读，默认值单源 ChainDefaults/WithdrawDefaults）。
/// </summary>
public class CollectionChainWorkerOptions
{
    public string? MasterKey { get; set; }

    public string? EtherscanV2Endpoint { get; set; } = "https://api.etherscan.io/v2/api";

    public Guid[] AlertRecipientUserIds { get; set; } = Array.Empty<Guid>();

    /// <summary>扫链/归集每轮墙钟预算（秒）。</summary>
    public int ChainRoundBudgetSeconds { get; set; } = 240;

    /// <summary>提现广播每轮墙钟预算（秒）。</summary>
    public int WithdrawRoundBudgetSeconds { get; set; } = 120;

    public static CollectionChainWorkerOptions FromConfiguration(IConfiguration configuration)
    {
        // 宿主键单源 = CollectionSettings.Host（完整冒号键从根配置直读；回退 = 本类默认值）
        var options = new CollectionChainWorkerOptions
        {
            MasterKey = configuration[Settings.CollectionSettings.Host.MasterKey],
            AlertRecipientUserIds = (configuration[Settings.CollectionSettings.Host.AlertRecipientUserIds] ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToArray(),
        };
        // 回退 = 本类默认值（初始化器里不能自引用属性，构造后回填；默认值单源=属性初始化器）
        options.EtherscanV2Endpoint =
            configuration[Settings.CollectionSettings.Host.EtherscanV2Endpoint] ?? options.EtherscanV2Endpoint;
        options.ChainRoundBudgetSeconds = configuration.GetValue(
            Settings.CollectionSettings.Host.ChainRoundBudgetSeconds, options.ChainRoundBudgetSeconds);
        options.WithdrawRoundBudgetSeconds = configuration.GetValue(
            Settings.CollectionSettings.Host.WithdrawRoundBudgetSeconds, options.WithdrawRoundBudgetSeconds);
        return options;
    }
}

/// <summary>
/// worker 注册扩展（计划第 59 行）：宿主调 AddCollectionChainWorkers；DbMigrator 不调——
/// 迁移进程不起调度器（HostServiceGraphValidationTests 钉住 DbMigrator 服务图无这些 hosted service）。
/// workers 无条件注册（默认关 = 纯睡眠）；开关热开后启动期无法预知，
/// Redis 支撑守卫由基类在轮内懒检查（CollectionChainWorkerBase.EnsureRedisBackedLockBackend，
/// fail-closed 拒绝执行 + [Alert:LockBackend] 告警）。
/// </summary>
public static class CollectionChainWorkersCollectionExtensions
{
    public static IServiceCollection AddCollectionChainWorkers(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = CollectionChainWorkerOptions.FromConfiguration(configuration);

        services.AddSingleton(options);
        services.AddSingleton<ChainAlertService>(sp => new ChainAlertService(
            options.AlertRecipientUserIds,
            sp.GetRequiredService<Volo.Abp.EventBus.Distributed.IDistributedEventBus>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ChainAlertService>>()));
        services.AddHostedService<DepositScanWorker>();
        services.AddHostedService<SweepWorker>();
        // 提现广播（M3）：同一 Redis 锁纪律；Collection.Withdraw.Enabled=false 时每轮纯睡眠（基类开关语义）
        services.AddHostedService<AbpAdmin.Collection.Withdrawals.WithdrawBroadcastWorker>();
        // 热钱包逐笔监控：Collection.Chain.HotWalletWatchEnabled=false 时每轮纯睡眠
        services.AddHostedService<HotWalletWatchWorker>();
        return services;
    }
}
