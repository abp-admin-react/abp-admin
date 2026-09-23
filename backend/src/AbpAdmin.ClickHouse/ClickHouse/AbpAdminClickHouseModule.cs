using System;
using System.Threading.Tasks;
using ClickHouse.Driver;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Modularity;

namespace AbpAdmin.ClickHouse;

/// <summary>
/// ClickHouse 接入模块(宿主级基础设施,由 AbpAdminHttpApiHostModule [DependsOn] 装配;
/// DbMigrator 不感知本模块)。
///
/// 职责与注册(仅在 <see cref="AbpAdminClickHouseOptions.IsUsable"/> 时生效,未启用对宿主零影响):
/// - <see cref="AbpAdminClickHouseOptions"/>:无条件注册单例(健康检查等处需要读配置判定);
/// - <see cref="IClickHouseClient"/>:官方客户端工厂注册单例(线程安全、内部 HTTP 连接池,
///   生命周期交由容器,宿主关闭时释放连接);
/// - <see cref="ClickHouseEventWriter"/>:同一实例注册为 IClickHouseEventWriter(业务面)
///   与 IHostedService(驱动后台冲刷循环),保证缓冲唯一。
///
/// 定位:主库(PG)仍是业务事实记录源,CH 只存采集/分析类数据(可重放的副本),
/// 刻意不接入 ABP 的 UnitOfWork/仓储体系。
/// </summary>
public class AbpAdminClickHouseModule : AbpModule
{
    /// <summary>
    /// 绑定配置并按开关完成注册。禁用(或连接串为空)时只注册 options,
    /// 不创建任何 CH 连接资源,健康检查也不注册(避免健康面板误报)。
    /// </summary>
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var options = configuration.GetSection(AbpAdminClickHouseOptions.SectionName)
                      .Get<AbpAdminClickHouseOptions>() ?? new AbpAdminClickHouseOptions();
        context.Services.AddSingleton(options);

        if (!options.IsUsable)
        {
            return;
        }

        // 工厂注册(而非预构造实例):保证宿主关闭时容器释放客户端持有的 HTTP 连接池
        context.Services.AddSingleton<IClickHouseClient>(sp => new ClickHouseClient(options.ConnectionString));

        // 写入器:单例注册 + IHostedService 驱动后台冲刷循环(同一实例,缓冲唯一)
        context.Services.AddSingleton<ClickHouseEventWriter>();
        context.Services.AddSingleton<IClickHouseEventWriter>(sp => sp.GetRequiredService<ClickHouseEventWriter>());
        context.Services.AddHostedService(sp => sp.GetRequiredService<ClickHouseEventWriter>());
    }

    /// <summary>
    /// 启动时建表(幂等 CREATE IF NOT EXISTS,DDL 见 <see cref="CollectedEventsTable"/>)。
    /// 失败姿态:CH 是辅助存储,不阻断宿主启动——表缺失由写入器失败路径按冷却期
    /// 自愈重跑幂等 DDL 收敛(见 <see cref="ClickHouseEventWriter.TryEnsureTableAsync"/>),
    /// 连接状态由健康检查持续暴露;此处失败只打 Error 留现场。
    /// </summary>
    public override async Task OnApplicationInitializationAsync(ApplicationInitializationContext context)
    {
        var options = context.ServiceProvider.GetRequiredService<AbpAdminClickHouseOptions>();
        if (!options.IsUsable)
        {
            return;
        }

        var logger = context.ServiceProvider.GetRequiredService<ILogger<AbpAdminClickHouseModule>>();
        try
        {
            var client = context.ServiceProvider.GetRequiredService<IClickHouseClient>();
            await client.ExecuteNonQueryAsync(CollectedEventsTable.BuildCreateTableSql(options.CollectedEventsTable));
            logger.LogInformation("ClickHouse 已就绪，采集事件表: {Table}", options.CollectedEventsTable);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ClickHouse 初始化失败（建表 {Table}），宿主继续启动，写入器会在 CH 恢复后自愈建表",
                options.CollectedEventsTable);
        }
    }
}
