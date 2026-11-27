using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SimpleStateChecking;
using Volo.Abp.Uow;

namespace AbpAdmin.Identity;

[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IRolePermissionAppService), typeof(IPermissionAppService), typeof(PermissionAppService))]
[RemoteService(false)]
public class RolePermissionAppService(
    IRolePermissionTarget target,
    IPermissionManager permissionManager,
    IPermissionChecker permissionChecker,
    IPermissionDefinitionManager permissionDefinitionManager,
    IResourcePermissionManager resourcePermissionManager,
    IResourcePermissionGrantRepository resourcePermissionGrantRepository,
    IOptions<PermissionManagementOptions> options,
    ISimpleStateCheckerManager<PermissionDefinition> simpleStateCheckerManager)
    : PermissionAppService(permissionManager, permissionChecker, permissionDefinitionManager, resourcePermissionManager,
        resourcePermissionGrantRepository, options, simpleStateCheckerManager), IRolePermissionAppService
{
    [Authorize(IdentityPermissions.Roles.ManagePermissions)]
    [UnitOfWork(isTransactional: true)]
    public virtual async Task<GetPermissionListResultDto> GetAsync(Guid id, string expectedRoleName)
    {
        var name = await target.LockAsync(id, expectedRoleName, forUpdate: false);
        return await base.GetAsync(RolePermissionValueProvider.ProviderName, name);
    }

    [Authorize(IdentityPermissions.Roles.ManagePermissions)]
    [UnitOfWork(isTransactional: true)]
    public virtual async Task UpdateAsync(Guid id, UpdateRolePermissionsDto input)
    {
        var name = await target.LockAsync(id, input.ExpectedRoleName, forUpdate: true);
        // Preserve ABP's provider policy, operator permission filtering, events and cache invalidation.
        await base.UpdateAsync(RolePermissionValueProvider.ProviderName, name, new UpdatePermissionsDto { Permissions = input.Permissions });
    }

    public override async Task UpdateAsync(string providerName, string providerKey, UpdatePermissionsDto input)
    {
        if (providerName == RolePermissionValueProvider.ProviderName)
        {
            await CheckProviderPolicy(providerName);
            throw new BusinessException(AbpAdminDomainErrorCodes.Identity.RolePermissionIdRequired);
        }

        await base.UpdateAsync(providerName, providerKey, input);
    }
}
