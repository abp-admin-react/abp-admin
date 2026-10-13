using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

/// <summary>
/// 角色下的用户只读查询。读门禁与「能看角色列表」同口径（AbpIdentity.Roles.Default）：
/// 只读视角不引入新权限面；改派操作仍走用户管理页的 Update 权限。
/// </summary>
[Authorize(IdentityPermissions.Roles.Default)]
public class RoleUserAdminAppService : ApplicationService, IRoleUserAdminAppService
{
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IIdentityUserRepository _userRepository;

    public RoleUserAdminAppService(
        IIdentityRoleRepository roleRepository,
        IIdentityUserRepository userRepository)
    {
        _roleRepository = roleRepository;
        _userRepository = userRepository;
    }

    public virtual async Task<PagedResultDto<RoleUserDto>> GetListAsync(Guid roleId, PagedAndSortedResultRequestDto input)
    {
        // 角色不存在返回 404（REST 语义），不静默给空列表——空列表会让前端误以为角色存在且无人使用
        var role = await _roleRepository.FindAsync(roleId)
                   ?? throw new EntityNotFoundException(typeof(IdentityRole), roleId);

        var totalCount = await _userRepository.GetCountAsync(roleId: role.Id);
        var users = await _userRepository.GetListAsync(
            input.Sorting ?? nameof(IdentityUser.UserName),
            input.MaxResultCount,
            input.SkipCount,
            roleId: role.Id);

        return new PagedResultDto<RoleUserDto>(
            totalCount,
            users.Select(u => new RoleUserDto
            {
                Id = u.Id,
                UserName = u.UserName,
                Name = u.Name,
                Email = u.Email,
                PhoneNumber = u.PhoneNumber,
                IsActive = u.IsActive,
                LockoutEnd = u.LockoutEnd,
                CreationTime = u.CreationTime
            }).ToList());
    }
}
