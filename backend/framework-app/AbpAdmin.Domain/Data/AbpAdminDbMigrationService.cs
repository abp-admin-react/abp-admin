using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp;
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
    // 框架与业务建表走各工程自己的 EF Core 迁移（Migrations/ 目录），由各 IAbpAdminDbSchemaMigrator 按各自 History 表执行；脚本时代存量库由 EfCoreLegacySchemaBaseliner 自动打戳。
    public async Task MigrateAsync()
    {
        var adminPassword = ResolveAdminPassword(_configuration);
        if (adminPassword == AbpAdminConsts.AdminPasswordDefaultValue)
        {
            Logger.LogWarning("Using the public template administrator password for local development.");
        }

        Logger.LogInformation("Started database migrations...");

        await MigrateDatabaseSchemaAsync();
        await SeedDataAsync(adminPassword);

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

                        await SeedDataAsync(adminPassword, tenant);
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

    internal static string ResolveAdminPassword(IConfiguration configuration)
    {
        var adminPassword = configuration["Identity:AdminPassword"];
        if (!string.IsNullOrWhiteSpace(adminPassword) &&
            adminPassword != AbpAdminConsts.AdminPasswordDefaultValue)
        {
            return adminPassword;
        }

        // Compose passes the actual frontend bind and its public OIDC origin.
        // In Compose, a Development override must not bypass either loopback check.
        var dockerBind = configuration["Identity:LocalDockerBindAddress"];
        var allowDefault = dockerBind is null
            ? configuration.GetValue<bool>("Identity:AllowDefaultAdminPassword")
            : IsLoopbackAddress(dockerBind) &&
              IsLoopbackOrigin(configuration["OpenIddict:Applications:AbpAdmin_App:RootUrl"]);

        if (!allowDefault)
        {
            throw new AbpInitializationException(
                "Identity:AdminPassword 必须设置为非模板口令。公开默认口令仅适用于回环 Docker 体验栈或显式允许的本地开发环境。");
        }

        return AbpAdminConsts.AdminPasswordDefaultValue;
    }

    private static bool IsLoopbackAddress(string host) =>
        IPAddress.TryParse(host.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address);

    private static bool IsLoopbackOrigin(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var origin) &&
        origin.Scheme is "http" or "https" &&
        origin.UserInfo.Length == 0 &&
        origin.AbsolutePath == "/" &&
        origin.Query.Length == 0 &&
        origin.Fragment.Length == 0 &&
        (origin.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IsLoopbackAddress(origin.Host));

    private async Task SeedDataAsync(string adminPassword, Tenant? tenant = null)
    {
        Logger.LogInformation($"Executing {(tenant == null ? "host" : tenant.Name + " tenant")} database seed...");

        await _dataSeeder.SeedAsync(new DataSeedContext(tenant?.Id)
            .WithProperty(IdentityDataSeedContributor.AdminEmailPropertyName,
                AbpAdminConsts.AdminEmailDefaultValue)
            .WithProperty(IdentityDataSeedContributor.AdminPasswordPropertyName,
                adminPassword)
            // 每个贡献者独立 UoW 且立即提交。DataSeeder.SeedAsync 自身的 [UnitOfWork]
            // 会让全部贡献者共用一个未提交 UoW，框架 PermissionDataSeedContributor 与我们的
            // SettingUi/FileManagement/DataScope 权限种子贡献者互相看不到对方的未提交授予行，
            // 去重失效后重复插入，租户种子在唯一索引上炸掉（host 因测试库把 NULL TenantId
            // 视为互不相同而静默通过——「空库跑通」只验 host 时暴露不出来）。
            // RequiresNew 必须为 true：否则子 UoW 并入环境 UoW，仍然攒到最后一次提交。
            //
            // 代价（问题10 关联说明）：SeedInSeparateUow + RequiresNew 意味着种子贡献者逐个独立
            // 提交、无整体原子性——中途失败会留下"半套数据"。由于各贡献者自身幂等（存在性检查
            // 后插入），失败后直接重跑 DbMigrator 即可续种。
            .WithProperty(DataSeederExtensions.SeedInSeparateUow, true)
            .WithProperty(DataSeederExtensions.SeedInSeparateUowRequiresNew, true)
        );
    }
}
