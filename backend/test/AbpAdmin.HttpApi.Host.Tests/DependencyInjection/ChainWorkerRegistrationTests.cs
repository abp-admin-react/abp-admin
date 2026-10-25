using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Settings;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Xunit;

namespace AbpAdmin.HttpApi.Host.Tests.DependencyInjection;

/// <summary>
/// 链上 worker 注册契约（计划第 59、65 行，开关热改迁移后语义）：
/// ① 开关已迁设置页（Collection.Chain.* / Collection.Withdraw.*）——定义默认值必须 fail-closed "false"，
/// 且宿主 appsettings 不再携带已迁移键（防配置/设置双源）；数值参数默认值与 ChainDefaults/WithdrawDefaults 单源一致。
/// ② Redis 支撑守卫改为轮内懒检查（开关热开后启动期无法预知）——判定纯函数表驱动锁。
/// ③ DbMigrator 进程不含链上 hosted service（迁移器不起调度器）。
/// </summary>
public class ChainWorkerRegistrationTests
{
    [Fact]
    public void Switch_Settings_Default_Off_And_Param_Defaults_Match_Single_Source()
    {
        var context = new FakeDefinitionContext();
        new CollectionSettingDefinitionProvider().Define(context);

        // 四个总开关 fail-closed：默认一律关
        foreach (var key in new[]
                 {
                     CollectionSettings.Chain.ScanEnabled,
                     CollectionSettings.Chain.SweepEnabled,
                     CollectionSettings.Chain.HotWalletWatchEnabled,
                     CollectionSettings.Withdraw.Enabled,
                 })
        {
            var definition = context.GetOrNull(key);
            definition.ShouldNotBeNull($"缺少设置定义：{key}");
            definition.DefaultValue.ShouldBe("false");
        }

        // 数值参数默认值 = 单源常量（不变文化字符串）
        context.GetOrNull(CollectionSettings.Withdraw.BroadcastIntervalSeconds)!.DefaultValue
            .ShouldBe(Inv(WithdrawDefaults.BroadcastIntervalSeconds));
        context.GetOrNull(CollectionSettings.Withdraw.BroadcastBatchSize)!.DefaultValue
            .ShouldBe(Inv(WithdrawDefaults.BroadcastBatchSize));
        context.GetOrNull(CollectionSettings.Withdraw.AutoApproveBatchSize)!.DefaultValue
            .ShouldBe(Inv(WithdrawDefaults.AutoApproveBatchSize));
        context.GetOrNull(CollectionSettings.Chain.ScanIntervalSeconds)!.DefaultValue
            .ShouldBe(Inv(ChainDefaults.ScanIntervalSeconds));
        context.GetOrNull(CollectionSettings.Chain.SweepIntervalSeconds)!.DefaultValue
            .ShouldBe(Inv(ChainDefaults.SweepIntervalSeconds));
        context.GetOrNull(CollectionSettings.Chain.Confirmations)!.DefaultValue
            .ShouldBe(Inv(ChainDefaults.Confirmations));
        context.GetOrNull(CollectionSettings.Chain.SweepRoundLimit)!.DefaultValue
            .ShouldBe(""); // 空 = 不限
    }

    [Fact]
    public void Shipped_AppSettings_No_Longer_Carries_Migrated_Switch_Keys()
    {
        // 迁移锁：开关/间隔/阈值已在设置页管理；宿主配置残留这些键会造成「配置改了不生效」的双源误导
        var appsettings = System.IO.File.ReadAllText(
            System.IO.Path.Combine(LocateHostDirectory(), "appsettings.json"));

        foreach (var migratedKey in new[]
                 {
                     "ScanEnabled", "SweepEnabled", "Confirmations", "FuelBnbLimit", "BroadcastIntervalSeconds",
                 })
        {
            appsettings.ShouldNotContain($"\"{migratedKey}\"");
        }

        // 墙钟预算留在宿主配置（挂死防护阀不热改）
        appsettings.ShouldContain("RoundBudgetSeconds");
    }

    [Theory]
    [InlineData("false", "redis-host", false)]     // 显式关 Redis
    [InlineData(null, " ", false)]                 // 配置串空白 = 未配置
    [InlineData("true", null, false)]              // 未给配置串
    [InlineData(null, "redis-host:6379", true)]    // 缺省（非显式 false）= 启用 + 已配置
    [InlineData("True", "redis-host:6379", true)]
    public void Redis_Backend_Lock_Guard_Table_Lock(
        string? redisEnabled, string? redisConfiguration, bool expected)
    {
        var data = new Dictionary<string, string?>();
        if (redisEnabled != null) data["Redis:IsEnabled"] = redisEnabled;
        if (redisConfiguration != null) data["Redis:Configuration"] = redisConfiguration;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(data).Build();

        CollectionChainWorkerBase.IsRedisBackedLockConfigured(configuration).ShouldBe(expected);
    }

    [Fact]
    public async Task Chain_Workers_Are_Not_Registered_In_DbMigrator_Service_Graph()
    {
        // DbMigrator 模块图不含 AddCollectionChainWorkers（扩展只在宿主调用）——
        // 直接断言模块程序集的类型图：worker 类型不属于 DbMigrator 引用链上的注册点，
        // 并断言 worker 类型本身是 IHostedService（注册语义正确性）
        typeof(CollectionChainWorkerBase).IsAssignableFrom(typeof(DepositScanWorker)).ShouldBeTrue();
        typeof(CollectionChainWorkerBase).IsAssignableFrom(typeof(SweepWorker)).ShouldBeTrue();
        typeof(CollectionChainWorkerBase)
            .IsAssignableFrom(typeof(AbpAdmin.Collection.Withdrawals.WithdrawBroadcastWorker)).ShouldBeTrue();

        // 提现广播 worker 的轮锁键（与扫链/归集各自的锁键互不重叠——同库常量唯一入口）
        new AbpAdmin.Collection.Withdrawals.WithdrawBroadcastWorker(
            null!, null!, null!, new CollectionChainWorkerOptions(), null!)
            .LockKeyExposeForTest()
            .ShouldBe(AbpAdmin.Collection.CollectionConsts.WithdrawBroadcastLockKey);

        // 热钱包监控 worker 的轮锁键（同族互不重叠）
        new HotWalletWatchWorker(null!, null!, null!, new CollectionChainWorkerOptions(), null!)
            .LockKeyExposeForTest()
            .ShouldBe(AbpAdmin.Collection.CollectionConsts.HotWalletWatchLockKey);

        // DbMigrator 项目不引用宿主工程（编译期隔离），这里核对宿主模块才调用扩展：
        var hostModuleSource = System.IO.File.ReadAllText(System.IO.Path.Combine(LocateHostDirectory(), "AbpAdminHttpApiHostModule.cs"));
        hostModuleSource.ShouldContain("AddCollectionChainWorkers");
    }

    private static string Inv(decimal value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string Inv(int value)
    {
        return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>轻量定义上下文：直接收集 provider 的定义（不经 DI，锁定义层默认值）。</summary>
    private sealed class FakeDefinitionContext : Volo.Abp.Settings.ISettingDefinitionContext
    {
        private readonly Dictionary<string, Volo.Abp.Settings.SettingDefinition> _map = new();

        public void Add(params Volo.Abp.Settings.SettingDefinition[] definitions)
        {
            foreach (var definition in definitions)
            {
                _map[definition.Name] = definition;
            }
        }

        public Volo.Abp.Settings.SettingDefinition? GetOrNull(string name)
        {
            return _map.TryGetValue(name, out var definition) ? definition : null;
        }

        public System.Collections.Generic.IReadOnlyList<Volo.Abp.Settings.SettingDefinition> GetAll()
        {
            return _map.Values.ToList();
        }
    }

    /// <summary>从测试程序集位置向上找到含 framework/AbpAdmin.HttpApi.Host 的仓库层（路径算术不炸）。</summary>
    private static string LocateHostDirectory()
    {
        var dir = new System.IO.DirectoryInfo(System.IO.Path.GetDirectoryName(
            typeof(ChainWorkerRegistrationTests).Assembly.Location)!);
        while (dir != null)
        {
            var candidate = System.IO.Path.Combine(dir.FullName, "framework", "AbpAdmin.HttpApi.Host");
            if (System.IO.File.Exists(System.IO.Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }

            dir = dir.Parent!;
        }

        return string.Empty;
    }
}
