using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using AbpAdmin.MultiTenancy;
using Volo.Abp.TenantManagement;

namespace AbpAdmin.Data;

public class AbpAdminDbMigrationService : ITransientDependency
{
    public ILogger<AbpAdminDbMigrationService> Logger { get; set; }

    private readonly IDataSeeder _dataSeeder;
    private readonly IEnumerable<IAbpAdminDbSchemaMigrator> _dbSchemaMigrators;
    private readonly ITenantRepository _tenantRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IConfiguration _configuration;

    public AbpAdminDbMigrationService(
        IDataSeeder dataSeeder,
        ITenantRepository tenantRepository,
        ICurrentTenant currentTenant,
        IEnumerable<IAbpAdminDbSchemaMigrator> dbSchemaMigrators,
        IConfiguration configuration)
    {
        _dataSeeder = dataSeeder;
        _tenantRepository = tenantRepository;
        _currentTenant = currentTenant;
        _dbSchemaMigrators = dbSchemaMigrators;
        _configuration = configuration;

        Logger = NullLogger<AbpAdminDbMigrationService>.Instance;
    }

    // 注意（问题9 修复）：模板遗留的 AddInitialMigrationIfNotExist/AddInitialMigration
    // （运行期调 abp CLI create-migration-and-run-migrator）已删除——生产 DbMigrator 不应携带
    // 开发机专属的建迁移路径，且其 catch (Exception) { return false; } 会静默吞错。
    // 初始迁移由开发者手动执行 dotnet ef migrations add 生成。
    public async Task MigrateAsync()
    {
        Logger.LogInformation("Started database migrations...");

        await MigrateDatabaseSchemaAsync();
        await SeedDataAsync();

        Logger.LogInformation($"Successfully completed host database migrations.");

        if (MultiTenancyConsts.IsEnabled)
        {
            var tenants = await _tenantRepository.GetListAsync(includeDetails: true);

            var migratedDatabaseSchemas = new HashSet<string>();
            var failures = new List<(string TenantName, Exception Error)>();

            foreach (var tenant in tenants)
            {
                // 租户故障隔离（问题9 修复）：单个租户迁移/种子失败不阻断其余租户，
                // 失败清单在循环结束后聚合成一个异常抛出（含租户名），运维可以看到完整失败面。
                try
                {
                    using (_currentTenant.Change(tenant.Id))
                    {
                        if (tenant.ConnectionStrings.Any())
                        {
                            var tenantConnectionStrings = tenant.ConnectionStrings
                                .Select(x => x.Value)
                                .ToList();

                            if (!migratedDatabaseSchemas.IsSupersetOf(tenantConnectionStrings))
                            {
                                await MigrateDatabaseSchemaAsync(tenant);

                                migratedDatabaseSchemas.AddIfNotContains(tenantConnectionStrings);
                            }
                        }

                        await SeedDataAsync(tenant);
                    }

                    Logger.LogInformation($"Successfully completed {tenant.Name} tenant database migrations.");
                }
                catch (Exception ex)
                {
                    failures.Add((tenant.Name, ex));
                    Logger.LogError(ex, $"Tenant '{tenant.Name}' database migration failed, continuing with remaining tenants.");
                }
            }

            if (failures.Count > 0)
            {
                throw new AggregateException(
                    $"{failures.Count} 个租户数据库迁移失败：{string.Join(", ", failures.Select(f => f.TenantName))}",
                    failures.Select(f => f.Error));
            }

            Logger.LogInformation("Successfully completed all database migrations.");
        }
        Logger.LogInformation("You can safely end this process...");
    }

    private async Task MigrateDatabaseSchemaAsync(Tenant? tenant = null)
    {
        Logger.LogInformation(
            $"Migrating schema for {(tenant == null ? "host" : tenant.Name + " tenant")} database...");

        foreach (var migrator in _dbSchemaMigrators)
        {
            // 记录参与迁移的每个实现：宿主上下文 + 各业务模块迁移器（IAbpAdminDbSchemaMigrator 约定）
            Logger.LogInformation($"Running schema migrator: {migrator.GetType().FullName}");
            await migrator.MigrateAsync();
        }
    }

    private async Task SeedDataAsync(Tenant? tenant = null)
    {
        Logger.LogInformation($"Executing {(tenant == null ? "host" : tenant.Name + " tenant")} database seed...");

        // 问题12 修复：种子 admin 密码可由配置覆盖（Identity:AdminPassword，DbMigrator/Host 均可配）；
        // 留空时回落模板默认值。生产部署必须注入强口令（公开默认值 = 任何人可登录）。
        var adminPassword = _configuration["Identity:AdminPassword"];
        if (string.IsNullOrWhiteSpace(adminPassword))
        {
            adminPassword = AbpAdminConsts.AdminPasswordDefaultValue;
            Logger.LogWarning(
                "Identity:AdminPassword 未配置，种子 admin 使用模板默认密码（公开已知，仅限开发环境）。生产部署请通过 appsettings.secrets.json 或环境变量 Identity__AdminPassword 注入强口令。");
        }

        await _dataSeeder.SeedAsync(new DataSeedContext(tenant?.Id)
            .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName,
                AbpAdminConsts.AdminEmailDefaultValue)
            .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName,
                adminPassword)
            // T3.4：每个贡献者独立 UoW 且立即提交。DataSeeder.SeedAsync 自身的 [UnitOfWork]
            // 会让全部贡献者共用一个未提交 UoW，框架 PermissionDataSeedContributor 与我们的
            // SettingUi/FileManagement/DataScope 权限种子贡献者互相看不到对方的未提交授予行，
            // 去重失效后重复插入，租户种子在唯一索引上炸掉（host 因 SQLite 把 NULL TenantId
            // 视为互不相同而静默通过——此前「空库跑通」只验了 host，一直没暴露）。
            // RequiresNew 必须为 true：否则子 UoW 并入环境 UoW，仍然攒到最后一次提交。
            //
            // 代价（问题10 关联说明）：SQLite 下 ABP UoW 事务被全局禁用
            // （见 AbpAdminEntityFrameworkCoreModule，框架对 SQLite 嵌套事务限制的标准做法），
            // SeedInSeparateUow + RequiresNew 意味着种子贡献者逐个独立提交、无整体原子性——
            // 中途失败会留下"半套数据"。由于各贡献者自身幂等（存在性检查后插入），
            // 失败后直接重跑 DbMigrator 即可续种；SQLite 开发库脏了就删库重跑。
            .WithProperty(DataSeederExtensions.SeedInSeparateUow, true)
            .WithProperty(DataSeederExtensions.SeedInSeparateUowRequiresNew, true)
        );
    }
}
