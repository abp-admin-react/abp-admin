using System;
using System.Threading.Tasks;
using AbpAdmin.Menus;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Identity;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 角色删除的级联清理。<see cref="MenuGrant"/>（ProviderName=R, ProviderKey=角色名）与
/// <see cref="RoleDataScope"/>（RoleName）都以角色名为键，角色删除后残留的行会静默失配
/// （fail-closed：菜单对受控用户消失、数据范围归零），管理端无任何提示——
/// 删除时同步清理，对齐 ABP Identity 自己对 PermissionGrants 的级联策略。
/// <para>角色重命名的同步未在此落地：ABP 实体事件（EntityUpdatedEventData&lt;IdentityRole&gt;）
/// 只携带新值，Domain 层拿不到重命名前的旧角色名；需要 IdentityRoleManager 扩展点或
/// EF OriginalValues 才能实现，超出本次改造范围（暂以「删除清理」兜住主要风险）。</para>
/// <para>RoleDataScope 的删除会触发 DataScopeCacheInvalidationHandler 递增 generation，
/// 数据范围缓存随之失效，无需额外处理。</para>
/// </summary>
public class RoleDeletedCascadeHandler :
    ILocalEventHandler<EntityDeletedEventData<IdentityRole>>,
    ITransientDependency
{
    private readonly IRepository<MenuGrant, Guid> _menuGrantRepository;
    private readonly IRepository<RoleDataScope, Guid> _roleDataScopeRepository;

    public RoleDeletedCascadeHandler(
        IRepository<MenuGrant, Guid> menuGrantRepository,
        IRepository<RoleDataScope, Guid> roleDataScopeRepository)
    {
        _menuGrantRepository = menuGrantRepository;
        _roleDataScopeRepository = roleDataScopeRepository;
    }

    public virtual async Task HandleEventAsync(EntityDeletedEventData<IdentityRole> eventData)
    {
        var roleName = eventData.Entity.Name;
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return;
        }

        var grants = await _menuGrantRepository.GetListAsync(
            x => x.ProviderName == MenuConsts.RoleProviderName && x.ProviderKey == roleName);
        foreach (var grant in grants)
        {
            await _menuGrantRepository.DeleteAsync(grant);
        }

        var scopes = await _roleDataScopeRepository.GetListAsync(x => x.RoleName == roleName);
        foreach (var scope in scopes)
        {
            await _roleDataScopeRepository.DeleteAsync(scope);
        }
    }
}
