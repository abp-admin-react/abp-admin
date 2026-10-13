using System;
using System.Threading.Tasks;
using AbpAdmin.Features;
using AbpAdmin.Menus;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.EventBus;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;

namespace AbpAdmin.Tenants;

/// <summary>
/// 租户删除后的资源清理（T2.8 SaaS Pro 缺口：删租户此前只删 AbpTenants 聚合，共享库内
/// 挂在租户名下的功能值、菜单、菜单角色勾选全部成为孤儿数据）：
/// - 功能值：AbpFeatureValues 中 ProviderName="T"、ProviderKey=租户 Id 的行；
/// - 菜单与菜单授权：AppMenus / AppMenuGrants 中 TenantId=租户 Id 的行（硬删除，
///   软删行会占用 (TenantId, Path) 唯一索引，同 Id 重建租户后懒拷贝会撞约束）。
///
/// 与 Pro 的边界一致：不 drop 独立租户数据库（数据库可能还有备份价值，删除是运维决策）；
/// 租户的 Identity 数据（用户/角色等框架表）清理量级大，留待独立工作项。
///
/// 本地实体事件在删除方的 UoW 内发布：清理与租户删除同一事务提交（PostgreSQL 原子），
/// 任一失败整体回滚。菜单删除须切回该租户上下文——租户行虽已删（且是软删除），
/// IMultiTenant 过滤只看 ICurrentTenant，与租户行是否还在无关。
/// </summary>
public class TenantDeletedResourceCleanupHandler :
    ILocalEventHandler<EntityDeletedEventData<Tenant>>,
    ITransientDependency
{
    private readonly FeatureValueCleanupService _featureValueCleanupService;
    private readonly MenuManager _menuManager;
    private readonly ICurrentTenant _currentTenant;

    public TenantDeletedResourceCleanupHandler(
        FeatureValueCleanupService featureValueCleanupService,
        MenuManager menuManager,
        ICurrentTenant currentTenant)
    {
        _featureValueCleanupService = featureValueCleanupService;
        _menuManager = menuManager;
        _currentTenant = currentTenant;
    }

    public virtual async Task HandleEventAsync(EntityDeletedEventData<Tenant> eventData)
    {
        var tenantId = eventData.Entity.Id;

        // 删除该租户名下全部功能值并失效缓存（策略收敛在 FeatureValueCleanupService）
        await _featureValueCleanupService.DeleteAllAsync(
            Volo.Abp.Features.TenantFeatureValueProvider.ProviderName,
            tenantId.ToString());

        using (_currentTenant.Change(tenantId))
        {
            await _menuManager.DeleteTenantMenusAsync(tenantId);
        }
    }
}
