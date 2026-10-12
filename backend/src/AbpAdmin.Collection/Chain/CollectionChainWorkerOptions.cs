using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// 链上 worker 运行参数（宿主配置；开关默认关——不配置即不跑，见计划第 5.6 节与 65 行守卫）。
/// </summary>
public class CollectionChainWorkerOptions
{
    public bool ScanEnabled { get; set; }

    public bool SweepEnabled { get; set; }

    public int ScanIntervalSeconds { get; set; } = 20;

    public int SweepIntervalSeconds { get; set; } = 60;

    /// <summary>每轮墙钟预算（秒）。</summary>
    public int RoundBudgetSeconds { get; set; } = 240;

    /// <summary>确认数：按测量与终局说明设定（BSC 0.75s/块，15 块 ≈ 11 秒 + 深度余量；不写死「至少 12」的教条，配置即口径）。</summary>
    public int Confirmations { get; set; } = 15;

    /// <summary>单轮块区间（测量 p95 150 条/块 × 50 = 7500 &lt; 0.8×cap 10000）。</summary>
    public int BlockRange { get; set; } = 50;

    /// <summary>归集每轮取出上限（null = 不限）。</summary>
    public decimal? SweepRoundLimit { get; set; }

    public decimal FuelBnbLimit { get; set; } = 0.005m;

    public decimal GasBnbAlertThreshold { get; set; } = 0.02m;

    public int RetryCooldownMinutes { get; set; } = 10;

    public int RebroadcastGraceMinutes { get; set; } = 30;

    public int SweepStuckMinutes { get; set; } = 120;

    public int ScanBehindBlocks { get; set; } = 2000;

    public int SkipRatioWindowMinutes { get; set; } = 1440;

    public decimal SkipRatioThreshold { get; set; } = 0.9m;

    public string? MasterKey { get; set; }

    public string? EtherscanV2Endpoint { get; set; } = "https://api.etherscan.io/v2/api";

    public Guid[] AlertRecipientUserIds { get; set; } = Array.Empty<Guid>();

    public static CollectionChainWorkerOptions FromConfiguration(IConfiguration configuration)
    {
        var s = configuration.GetSection("Collection:Chain");
        var options = new CollectionChainWorkerOptions
        {
            ScanEnabled = s.GetValue("ScanEnabled", false),
            SweepEnabled = s.GetValue("SweepEnabled", false),
            ScanIntervalSeconds = s.GetValue("ScanIntervalSeconds", 20),
            SweepIntervalSeconds = s.GetValue("SweepIntervalSeconds", 60),
            RoundBudgetSeconds = s.GetValue("RoundBudgetSeconds", 240),
            Confirmations = s.GetValue("Confirmations", 15),
            BlockRange = s.GetValue("BlockRange", 50),
            SweepRoundLimit = s.GetValue<decimal?>("SweepRoundLimit"),
            FuelBnbLimit = s.GetValue("FuelBnbLimit", 0.005m),
            GasBnbAlertThreshold = s.GetValue("GasBnbAlertThreshold", 0.02m),
            RetryCooldownMinutes = s.GetValue("RetryCooldownMinutes", 10),
            RebroadcastGraceMinutes = s.GetValue("RebroadcastGraceMinutes", 30),
            SweepStuckMinutes = s.GetValue("SweepStuckMinutes", 120),
            ScanBehindBlocks = s.GetValue("ScanBehindBlocks", 2000),
            SkipRatioWindowMinutes = s.GetValue("SkipRatioWindowMinutes", 1440),
            SkipRatioThreshold = s.GetValue("SkipRatioThreshold", 0.9m),
            MasterKey = configuration["Collection:MasterKey"],
            EtherscanV2Endpoint = s["EtherscanV2Endpoint"] ?? "https://api.etherscan.io/v2/api",
            AlertRecipientUserIds = (s["AlertRecipientUserIds"] ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .ToArray(),
        };
        return options;
    }
}

/// <summary>
/// worker 注册扩展（计划第 59 行）：宿主调 AddCollectionChainWorkers；DbMigrator 不调——
/// 迁移进程不起调度器（HostServiceGraphValidationTests 钉住 DbMigrator 服务图无这两个 hosted service）。
/// 守卫（第 65 行）：开关为开而 Redis 不是「由 Redis 支撑的分布式锁」时启动失败——
/// 判定读配置（Redis:IsEnabled 缺失/空 = 启用，显式 false = 关闭；Redis:Configuration 空白 = 未配置），
/// 不在运行时判断锁对象类型。非法值抛 AbpInitializationException。
/// </summary>
public static class CollectionChainWorkersCollectionExtensions
{
    public static IServiceCollection AddCollectionChainWorkers(
        this IServiceCollection services, IConfiguration configuration)
    {
        var options = CollectionChainWorkerOptions.FromConfiguration(configuration);

        if (options.ScanEnabled || options.SweepEnabled)
        {
            GuardRedisBackedLock(configuration);
        }

        services.AddSingleton(options);
        services.AddSingleton<ChainAlertService>(sp => new ChainAlertService(
            options.AlertRecipientUserIds,
            sp.GetRequiredService<Volo.Abp.EventBus.Distributed.IDistributedEventBus>(),
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ChainAlertService>>()));
        services.AddHostedService<DepositScanWorker>();
        services.AddHostedService<SweepWorker>();
        return services;
    }

    private static void GuardRedisBackedLock(IConfiguration configuration)
    {
        var redisEnabledValue = configuration["Redis:IsEnabled"];
        var redisEnabled = !string.Equals(redisEnabledValue, "false", StringComparison.OrdinalIgnoreCase);
        var redisConfiguration = configuration["Redis:Configuration"];

        if (!redisEnabled || string.IsNullOrWhiteSpace(redisConfiguration))
        {
            throw new Volo.Abp.AbpInitializationException(
                "扫链/归集已开启，但分布式锁不是 Redis 支撑（Redis:IsEnabled 显式 false，或 Redis:Configuration 空白）。" +
                "多实例下进程挂死会永久占锁/丢轮；请先启用 Redis，或先关闭扫链与归集开关（计划第 5.6 节守卫）。");
        }
    }
}
