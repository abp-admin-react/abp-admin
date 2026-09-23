using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.Tenants;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Data;

/// <summary>
/// 收到 <see cref="TenantDatabaseMigrationNeededEto"/> 后：建独立租户库（若缺）→ 迁移 schema。
/// 与 DbMigrator 的逐租户迁移同一机制（IAbpAdminDbSchemaMigrator 在当前租户上下文里
/// 解析该租户的连接串跑建表脚本迁移），幂等、可重跑。
///
/// 范围决策（有意为之，勿"补全"）：
/// - 只处理默认连接串——主业务 DbContext（[ConnectionStringName("Default")]）的落点；
///   模块独立库（EasyAbpFileManagement 等）迁移链路不同，暂不覆盖；
/// - 只建库+迁 schema，不搬数据：存量租户切独立库后新库为空（与 Pro 行为一致）；
/// - 不 drop 库：删租户不删库（见 TenantDeletedResourceCleanupHandler）。
///
/// [UnitOfWork(IsDisabled = true)]：分布式事件在提交方的 UoW 完成阶段投递，禁用后
/// 租户读取走显式的新 UoW（读到已提交连接串），建表迁移则完全脱离 UoW——与 DbMigrator
/// 运行条件一致。
///
/// 已知取舍（当前部署形态）：项目未接外部 MQ，IDistributedEventBus 回落
/// LocalDistributedEventBus——迁移在保存连接串的请求内同步执行，PostgreSQL 全新库
/// 可能阻塞该请求数十秒（SQLite ~200ms）；失败被吞掉只记日志，管理端看到的是保存
/// 成功而迁移结果未知。后台化（IBackgroundJobManager/真 MQ）是既定演进方向，
/// 在此之前靠：管理员低频操作 + 保存前「测试连接」预检 + 失败日志 + 幂等重试。
///
/// 失败处理：吞异常只记日志，不重抛——连接串保存已提交、调用方无从补救，迁移失败通过
/// 日志暴露。重试路径：重新保存连接串即重发事件（两个写端点都是「提交明文 Default
/// 即发布」，不要求值变化），或离线重跑 DbMigrator——两者均幂等。
/// </summary>
public class TenantDatabaseMigrationNeededHandler :
    IDistributedEventHandler<TenantDatabaseMigrationNeededEto>,
    ITransientDependency
{
    public ILogger<TenantDatabaseMigrationNeededHandler> Logger { get; set; }

    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantDatabaseCreator _databaseCreator;
    private readonly TenantConnectionStringProtector _connectionStringProtector;
    private readonly IEnumerable<IAbpAdminDbSchemaMigrator> _dbSchemaMigrators;
    private readonly ICurrentTenant _currentTenant;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public TenantDatabaseMigrationNeededHandler(
        ITenantRepository tenantRepository,
        ITenantDatabaseCreator databaseCreator,
        TenantConnectionStringProtector connectionStringProtector,
        IEnumerable<IAbpAdminDbSchemaMigrator> dbSchemaMigrators,
        ICurrentTenant currentTenant,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _tenantRepository = tenantRepository;
        _databaseCreator = databaseCreator;
        _connectionStringProtector = connectionStringProtector;
        _dbSchemaMigrators = dbSchemaMigrators;
        _currentTenant = currentTenant;
        _unitOfWorkManager = unitOfWorkManager;

        Logger = NullLogger<TenantDatabaseMigrationNeededHandler>.Instance;
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task HandleEventAsync(TenantDatabaseMigrationNeededEto eventData)
    {
        try
        {
            Tenant? tenant;
            using (var uow = _unitOfWorkManager.Begin(requiresNew: true))
            {
                tenant = await _tenantRepository.FindAsync(eventData.TenantId);
                await uow.CompleteAsync();
            }

            if (tenant == null)
            {
                Logger.LogInformation(
                    "Tenant {TenantId} no longer exists, skipping database creation/migration.",
                    eventData.TenantId);
                return;
            }

            var stored = tenant.FindDefaultConnectionString();
            if (stored == null)
            {
                Logger.LogInformation(
                    "Tenant {TenantName} uses the shared database, skipping database creation/migration.",
                    tenant.Name);
                return;
            }

            var connectionString = _connectionStringProtector.DecryptOrPlain(stored);

            Logger.LogInformation("Ensuring database exists for tenant {TenantName}...", tenant.Name);
            await _databaseCreator.CreateIfNotExistsAsync(connectionString);

            Logger.LogInformation("Migrating schema for tenant {TenantName} database...", tenant.Name);
            using (_currentTenant.Change(eventData.TenantId, tenant.Name))
            {
                foreach (var migrator in _dbSchemaMigrators)
                {
                    await migrator.MigrateAsync();
                }
            }

            Logger.LogInformation(
                "Tenant {TenantName} database ensured and migrated successfully.", tenant.Name);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Tenant database creation/migration failed for tenant {TenantId}. " +
                "Retry by re-saving the tenant connection string or re-running DbMigrator (idempotent).",
                eventData.TenantId);
        }
    }
}
