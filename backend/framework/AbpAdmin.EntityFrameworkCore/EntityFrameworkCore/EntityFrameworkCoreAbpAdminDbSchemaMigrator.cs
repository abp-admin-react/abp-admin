using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AbpAdmin.Data;
using Npgsql;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// 框架库 schema 迁移器（IAbpAdminDbSchemaMigrator 实现，被迁移循环自动枚举）。三步：
/// ① PostgresStartupPreflight.EnsureConnectableAsync——启动预检：占位串/连不上/库不存在
///    都在此拒绝启动并给出可行动指引（本系统不自动建库，建库是部署侧一次性动作）；
/// ② EfCoreLegacySchemaBaseliner.StampIfNeededAsync——存量库自举：脚本时代（迁移机制
///    引入前）建的库表在而 __EFMigrationsHistory 缺失，MigrateAsync 会重放 Initial 撞
///    42P07「表已存在」。探测到该形态时把 Initial 记账为已应用（baseline 打戳）而非重放
///    ——与 nopCommerce(MigrationVersionInfo)/Umbraco(umbracoMigration) 及微软「存量库
///    接入迁移」指引同模式。
/// ③ AbpAdminDbContext.Database.MigrateAsync()——执行本工程 Migrations/ 下的 EF Core 迁移。
/// ④ QuartzTablesEnsurer.EnsureCreatedAsync——qrtz_* 持久化表幂等建表（Quartz 自管 schema，
///    不属任何 DbContext 模型；UsePersistentStore=true 的 Production 姿势依赖这些表，
///    缺表时宿主启动的 performSchemaValidation 直接失败）。
/// <see cref="HasPendingAsync"/> 供宿主启动检查：History 表与当前模型相比是否还有未应用迁移。
/// DbContext 经容器解析（非构造注入），租户循环下的连接串切换照常生效。
/// </summary>
public class EntityFrameworkCoreAbpAdminDbSchemaMigrator
    : IAbpAdminDbSchemaMigrator, ITransientDependency
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ICurrentTenant _currentTenant;

    public EntityFrameworkCoreAbpAdminDbSchemaMigrator(
        IServiceProvider serviceProvider,
        ICurrentTenant currentTenant)
    {
        _serviceProvider = serviceProvider;
        _currentTenant = currentTenant;
    }

    public async Task MigrateAsync()
    {
        /* We intentionally resolving the AbpAdminDbContext
         * from IServiceProvider (instead of directly injecting it)
         * to properly get the connection string of the current tenant in the
         * current scope.
         */

        var dbContext = _serviceProvider.GetRequiredService<AbpAdminDbContext>();
        var logger = _serviceProvider.GetRequiredService<ILogger<EntityFrameworkCoreAbpAdminDbSchemaMigrator>>();

        // 预检最前：占位串/连不上/库不存在都在此拒绝启动并给出可行动指引（自动迁移与深层堆栈之前）
        await PostgresStartupPreflight.EnsureConnectableAsync(
            (NpgsqlConnection)dbContext.Database.GetDbConnection(), logger);

        await EfCoreLegacySchemaBaseliner.StampIfNeededAsync(
            dbContext.Database.GetDbConnection(),
            logger,
            historyTableName: EfCoreLegacySchemaBaseliner.FrameworkHistoryTable,
            constraintName: "PK___EFMigrationsHistory",
            initialMigrationId: EfCoreLegacySchemaBaseliner.FrameworkInitialMigrationId,
            baselineProductVersion: "10.0.9",
            sentinelTableName: EfCoreLegacySchemaBaseliner.FrameworkSentinelTable);

        await dbContext.Database.MigrateAsync();

        // ④：qrtz_* 持久化表只在宿主库建——Quartz 持久化 store 固定读 Default 连接串
        // （宿主模块配置），租户库用不上；迁移循环会带租户上下文逐库调用本方法，
        // 这里按租户上下文门控，避免给每个租户库白建 11 表 + 20 索引。
        // MigrateAsync 结束后连接由 EF 归还关闭状态，EnsureCreatedAsync 需要时自行打开
        if (_currentTenant.Id == null)
        {
            await QuartzTablesEnsurer.EnsureCreatedAsync(
                (NpgsqlConnection)dbContext.Database.GetDbConnection(), logger);
        }
    }

    public async Task<bool> HasPendingAsync()
    {
        // 库不存在 / History 表缺失时 GetPendingMigrationsAsync 会抛错，
        // 调用方（宿主启动检查）统一视为「需要迁移」——与接口契约一致
        var dbContext = _serviceProvider.GetRequiredService<AbpAdminDbContext>();
        return (await dbContext.Database.GetPendingMigrationsAsync()).Any();
    }
}
