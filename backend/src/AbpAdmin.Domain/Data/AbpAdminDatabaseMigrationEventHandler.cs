using System.Threading.Tasks;
using AbpAdmin.DataDictionaries;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events.Distributed;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Data;

/// <summary>
/// 新建租户后为该租户补跑数据字典种子（T3.4 第 10 步）。
/// 数据字典模块没有「租户查不到回落到 host」的逻辑（已核实：CacheDataDictionaryValueProvider
/// 与 DataDictionaryAppService 全程没有 Disable&lt;IMultiTenant&gt; 也没有切 host），
/// host 种的字典租户看不见，必须逐租户种。DbMigrator 只覆盖部署时已存在的租户，
/// 运行期新建的租户靠这个事件补齐。
///
/// 事件选型（实测更正：04 规格原稿说「TenantCreatedEto 在 ABP 10.6 已不存在」，失实——
/// 反编译确认 10.6.0 基类 TenantAppService.CreateAsync 确实发布 TenantCreatedEto，
/// 只是 OSS 模块内没有它的处理器。本类不订阅它，仍选 EntityCreatedEto&lt;TenantEto&gt;）：
/// TenantManagement 模块注册了 EtoMappings.Add&lt;Tenant, TenantEto&gt;，
/// 插入 Tenant 聚合时框架自动发布分布式事件 EntityCreatedEto&lt;TenantEto&gt;
/// （EntityChangeEventHelper.PublishEntityCreatedEvent），任何建租户路径都会触发，
/// 不限于 TenantAppService 一条。
///
/// 为什么只跑字典两个贡献者而不是规格原文的全量 IDataSeeder.SeedAsync（实测结论）：
/// 全量种子在事件上下文里会连环触发其它模块种子贡献者的潜伏冲突——
/// ① 框架 PermissionDataSeedContributor 与 T1 自研的 SettingUi/FileManagement/DataScope
///    权限种子在同一 UoW 里互相看不到未提交行，去重失效重复插入（AbpPermissionGrants 唯一索引）；
/// ② IdentityDataSeedContributor 建租户 admin 用户触发的 UserEto 事件会让 FileManagement 的
///    FileUser 同步器重复插入（EasyAbpFileManagementUsers 主键）。
/// 这些都不是字典该修的。租户 admin 用户由 TenantAppService 建租户时自己创建；
/// 其余按租户的种子（定时作业等）仍是 DbMigrator 的职责，与 T3.4 之前一致、无回退。
///
/// [UnitOfWork(IsDisabled = true)]：事件处理器会被 UoW 拦截器包住（建租户请求路径下还会并入
/// 正在完成的请求 UoW）。禁掉后两个贡献者各自的 [UnitOfWork] 独立开 UoW、随做随提交。
/// 排障提示：新建租户后如果下拉是空的，先检查本处理器是否执行过（或对该租户重跑 DbMigrator）。
/// </summary>
public class AbpAdminDatabaseMigrationEventHandler
    : IDistributedEventHandler<EntityCreatedEto<TenantEto>>, ITransientDependency
{
    private readonly DataDictionaryDataSeedContributor _handWrittenContributor;
    private readonly EnumDataDictionarySyncDataSeedContributor _syncContributor;
    private readonly ICurrentTenant _currentTenant;

    public AbpAdminDatabaseMigrationEventHandler(
        DataDictionaryDataSeedContributor handWrittenContributor,
        EnumDataDictionarySyncDataSeedContributor syncContributor,
        ICurrentTenant currentTenant)
    {
        _handWrittenContributor = handWrittenContributor;
        _syncContributor = syncContributor;
        _currentTenant = currentTenant;
    }

    [UnitOfWork(IsDisabled = true)]
    public virtual async Task HandleEventAsync(EntityCreatedEto<TenantEto> eventData)
    {
        using (_currentTenant.Change(eventData.Entity.Id))
        {
            await _handWrittenContributor.SeedAsync(new DataSeedContext(eventData.Entity.Id));
            await _syncContributor.SeedAsync(new DataSeedContext(eventData.Entity.Id));
        }
    }
}
