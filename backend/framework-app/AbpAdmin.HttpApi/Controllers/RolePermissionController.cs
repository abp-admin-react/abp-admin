using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Controllers;

[Route("api/identity/roles/{id:guid}/permissions")]
[ClientErrorAudience(ClientErrorAudience.AdminCompatibility)]
[Authorize(IdentityPermissions.Roles.ManagePermissions)]
public class RolePermissionController(IRolePermissionAppService permissions, IUnitOfWorkManager units) : AbpAdminController
{
    [HttpGet]
    [UnitOfWork(IsDisabled = true)]
    public async Task<GetPermissionListResultDto> GetAsync(Guid id, [FromQuery, Required] string expectedRoleName)
    {
        // A GET may already have a non-transactional UoW. Hold the role's shared lock in a new
        // transaction until ABP finishes reading the permissions keyed by its name.
        using var read = units.Begin(requiresNew: true, isTransactional: true);
        var result = await permissions.GetAsync(id, expectedRoleName);
        await read.CompleteAsync(HttpContext.RequestAborted);
        return result;
    }

    [HttpPut]
    public Task UpdateAsync(Guid id, [FromBody] UpdateRolePermissionsDto input)
        => permissions.UpdateAsync(id, input);
}
