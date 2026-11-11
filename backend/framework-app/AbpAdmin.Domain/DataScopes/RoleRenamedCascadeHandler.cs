using System;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 角色重命名时同步以角色名为键的 RoleDataScope；一角色一行，直接更新规范角色名。
/// <para>事件与处理方式对齐 ABP 官方：IdentityRole.ChangeName 发布分布式事件
/// <see cref="IdentityRoleNameChangedEto"/>（携带 OldName/Name，单机部署下 distributed bus
/// 是进程内实现，无需真实 broker），PermissionManagement 模块自己的
/// RoleUpdateEventHandler 就是用它把 PermissionGrants 的 ProviderKey 从旧名改到新名。
/// 本类同时更新自定义数据范围表，互不干扰。</para>
/// <para>[UnitOfWork]：事件在源工作单元完成之后分发，处理器需要自己的工作单元写库
/// （与上游 RoleDeletedEventHandler 同款处理）。</para>
/// <para>RoleDataScope 改键触发 EntityUpdated 事件 → DataScopeCacheInvalidationHandler
/// 递增 generation，数据范围缓存随之失效。</para>
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
