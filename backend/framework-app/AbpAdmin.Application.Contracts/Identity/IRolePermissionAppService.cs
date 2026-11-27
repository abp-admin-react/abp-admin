using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;
using Volo.Abp.PermissionManagement;

namespace AbpAdmin.Identity;

public interface IRolePermissionAppService : IApplicationService
{
    Task<GetPermissionListResultDto> GetAsync(Guid id, string expectedRoleName);
    Task UpdateAsync(Guid id, UpdateRolePermissionsDto input);
}

public class UpdateRolePermissionsDto
{
    [Required]
    public string ExpectedRoleName { get; set; } = string.Empty;

    [Required]
    public required UpdatePermissionDto[] Permissions { get; set; }
}
