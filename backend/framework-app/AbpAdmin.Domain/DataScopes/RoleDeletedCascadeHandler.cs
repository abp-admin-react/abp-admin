using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus;
using Volo.Abp.Identity;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 角色删除时清理以角色名为键的自定义数据范围，避免孤儿记录。
/// <para>角色重命名的同步由 <see cref="RoleRenamedCascadeHandler"/> 负责
/// （订阅 IdentityRoleNameChangedEto，与上游 PermissionManagement 的 RoleUpdateEventHandler 同构）。</para>
/// <para>RoleDataScope 的删除会触发 DataScopeCacheInvalidationHandler 递增 generation，
/// 数据范围缓存随之失效，无需额外处理。</para>
/// </summary>
public class RoleDeletedCascadeHandler :
    ILocalEventHandler<EntityDeletedEventData<IdentityRole>>,
    ITransientDependency
{
    private readonly IRepository<RoleDataScope, Guid> _roleDataScopeRepository;

    public RoleDeletedCascadeHandler(
        IRepository<RoleDataScope, Guid> roleDataScopeRepository)
    {
        _roleDataScopeRepository = roleDataScopeRepository;
    }

    public virtual async Task HandleEventAsync(EntityDeletedEventData<IdentityRole> eventData)
    {
        var roleName = eventData.Entity.Name;
        if (string.IsNullOrWhiteSpace(roleName))
        {
            return;
        }

        var scopes = await _roleDataScopeRepository.GetListAsync(x => x.RoleName == roleName);
        foreach (var scope in scopes)
        {
            await _roleDataScopeRepository.DeleteAsync(scope);
        }
    }
}
