using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 收到 <see cref="IdentityRoleNameChangedEto"/> 时，将 RoleDataScope.RoleName 从旧名更新为新名。
/// 当前宿主在改名事务提交前通过进程内总线分发该事件，ABP 的权限处理器在同一事务中更新角色授权键。
/// </summary>
public class RoleRenamedCascadeHandler :
    IDistributedEventHandler<IdentityRoleNameChangedEto>,
    ITransientDependency
{
    private readonly IRepository<RoleDataScope, Guid> _roleDataScopeRepository;

    public RoleRenamedCascadeHandler(
        IRepository<RoleDataScope, Guid> roleDataScopeRepository)
    {
        _roleDataScopeRepository = roleDataScopeRepository;
    }

    [UnitOfWork]
    public virtual async Task HandleEventAsync(IdentityRoleNameChangedEto eventData)
    {
        if (string.IsNullOrWhiteSpace(eventData.OldName) || eventData.OldName == eventData.Name)
        {
            return;
        }

        var scopes = await _roleDataScopeRepository.GetListAsync(x => x.RoleName == eventData.OldName);
        foreach (var scope in scopes)
        {
            scope.ChangeRoleName(eventData.Name);
            await _roleDataScopeRepository.UpdateAsync(scope);
        }
    }
}
