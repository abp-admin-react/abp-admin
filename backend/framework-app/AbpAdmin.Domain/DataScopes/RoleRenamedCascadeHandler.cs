using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Menus;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 角色重命名的级联同步。<see cref="MenuGrant"/>（ProviderName=R, ProviderKey=角色名）与
/// <see cref="RoleDataScope"/>（RoleName）都以角色名为键，角色重命名后残留旧行会静默失配
/// （fail-closed：菜单对角色成员消失、数据范围归零），管理端无任何提示——重命名时同步：
/// MenuGrant 行按<strong>合并语义</strong>处理（目标名已有同菜单授权——孤儿残留或竞态——
/// 则删旧行保新行，避免撞 (MenuId, ProviderName, ProviderKey) 唯一索引让整个改名失败）；
/// RoleDataScope 一角色一行、目标名行在角色名唯一约束下不可能并存，直接改键。
/// <para>事件与处理方式对齐 ABP 官方：IdentityRole.ChangeName 发布分布式事件
/// <see cref="IdentityRoleNameChangedEto"/>（携带 OldName/Name，单机部署下 distributed bus
/// 是进程内实现，无需真实 broker），PermissionManagement 模块自己的
/// RoleUpdateEventHandler 就是用它把 PermissionGrants 的 ProviderKey 从旧名改到新名。
/// 本类是同一事件在我们的两张自研表上的第二个订阅者，互不干扰。</para>
/// <para>[UnitOfWork]：事件在源工作单元完成之后分发，处理器需要自己的工作单元写库
/// （与上游 RoleDeletedEventHandler 同款处理）。</para>
/// <para>RoleDataScope 改键触发 EntityUpdated 事件 → DataScopeCacheInvalidationHandler
/// 递增 generation，数据范围缓存随之失效；MenuGrant 无缓存，直接生效。</para>
/// </summary>
public class RoleRenamedCascadeHandler :
    IDistributedEventHandler<IdentityRoleNameChangedEto>,
    ITransientDependency
{
    private readonly IRepository<MenuGrant, Guid> _menuGrantRepository;
    private readonly IRepository<RoleDataScope, Guid> _roleDataScopeRepository;

    public RoleRenamedCascadeHandler(
        IRepository<MenuGrant, Guid> menuGrantRepository,
        IRepository<RoleDataScope, Guid> roleDataScopeRepository)
    {
        _menuGrantRepository = menuGrantRepository;
        _roleDataScopeRepository = roleDataScopeRepository;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(IdentityRoleNameChangedEto eventData)
    {
        if (string.IsNullOrWhiteSpace(eventData.OldName) || eventData.OldName == eventData.Name)
        {
            return;
        }

        // 合并语义而非直接改键：目标名（新角色名）若在同一菜单上已有授权行（历史孤儿或
        // 竞态残留——角色名唯一约束保证改名时不存在同名在册角色，正常流程撞不上），
        // 直接 ChangeProviderKey 会撞 (MenuId, ProviderName, ProviderKey) 唯一索引让整个
        // 改名失败；此处删旧行、保留目标名行——两行表达同一事实（该菜单对该角色名授权）。
        var grants = await _menuGrantRepository.GetListAsync(
            x => x.ProviderName == MenuConsts.RoleProviderName && x.ProviderKey == eventData.OldName);
        if (grants.Count > 0)
        {
            var targetNameMenuIds = (await _menuGrantRepository.GetListAsync(
                    x => x.ProviderName == MenuConsts.RoleProviderName && x.ProviderKey == eventData.Name))
                .Select(x => x.MenuId)
                .ToHashSet();

            foreach (var grant in grants)
            {
                if (targetNameMenuIds.Contains(grant.MenuId))
                {
                    await _menuGrantRepository.DeleteAsync(grant);
                }
                else
                {
                    grant.ChangeProviderKey(eventData.Name);
                    await _menuGrantRepository.UpdateAsync(grant);
                }
            }
        }

        var scopes = await _roleDataScopeRepository.GetListAsync(x => x.RoleName == eventData.OldName);
        foreach (var scope in scopes)
        {
            scope.ChangeRoleName(eventData.Name);
            await _roleDataScopeRepository.UpdateAsync(scope);
        }
    }
}
