using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

[Authorize(AbpAdminPermissions.OrganizationUnits.Default)]
public class OrganizationUnitAppService : AbpAdminAppService, IOrganizationUnitAppService
{
    private readonly OrganizationUnitManager _organizationUnitManager;
    private readonly IOrganizationUnitRepository _organizationUnitRepository;
    private readonly IRepository<IdentityUser, Guid> _userQueryRepository;
    private readonly IdentityUserManager _userManager;
    private readonly IIdentityRoleRepository _roleRepository;

    public OrganizationUnitAppService(
        OrganizationUnitManager organizationUnitManager,
        IOrganizationUnitRepository organizationUnitRepository,
        IRepository<IdentityUser, Guid> userQueryRepository,
        IdentityUserManager userManager,
        IIdentityRoleRepository roleRepository)
    {
        _organizationUnitManager = organizationUnitManager;
        _organizationUnitRepository = organizationUnitRepository;
        _userQueryRepository = userQueryRepository;
        _userManager = userManager;
        _roleRepository = roleRepository;
    }

    public virtual async Task<System.Collections.Generic.List<OrganizationUnitDto>> GetListAsync()
    {
        var units = await _organizationUnitRepository.GetListAsync(includeDetails: false);
        return units
            .OrderBy(x => x.Code)
            .Select(MapToDto)
            .ToList();
    }

    [Authorize(AbpAdminPermissions.OrganizationUnits.Create)]
    public virtual async Task<OrganizationUnitDto> CreateAsync(CreateOrganizationUnitDto input)
    {
        var ou = new OrganizationUnit(
            GuidGenerator.Create(),
            input.DisplayName,
            input.ParentId,
            CurrentTenant.Id);
        await _organizationUnitManager.CreateAsync(ou);
        return MapToDto(ou);
    }

    [Authorize(AbpAdminPermissions.OrganizationUnits.Update)]
    public virtual async Task<OrganizationUnitDto> UpdateAsync(Guid id, UpdateOrganizationUnitDto input)
    {
        var ou = await _organizationUnitRepository.GetAsync(id);
        ou.DisplayName = input.DisplayName;
        await _organizationUnitManager.UpdateAsync(ou);
        return MapToDto(ou);
    }

    [Authorize(AbpAdminPermissions.OrganizationUnits.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await _organizationUnitManager.DeleteAsync(id);
    }

    /// <summary>
    /// 移动组织单元。父子关系走请求体（MoveOrganizationUnitInput）而非路由段：
    /// 「移到根级」的 parentId=null 在路由段里无法表达，DTO 语义天然完整。
    /// </summary>
    [Authorize(AbpAdminPermissions.OrganizationUnits.Update)]
    public virtual async Task MoveAsync(Guid id, MoveOrganizationUnitInput input)
    {
        var ou = await _organizationUnitRepository.GetAsync(id);
        if (input.ParentId.HasValue)
        {
            // 防环守卫（UI 不是控制，必须在服务端做）：ABP OrganizationUnitManager.MoveAsync
            // 只重算 Code、不校验后代，移入自身子树会让 Code 以自身旧前缀重算，
            // 层级树渲染、Code 前缀查询、OU 派生角色与数据范围全部失真且难以回滚。
            // OU Code 是祖先链物化路径：目标父的 Code 以本节点 Code 为前缀 ⇒ 目标在本节点子树内。
            // 已知限制（六透镜第二轮定性为可接受）：守卫读 Code 与 Manager 重算 Code 之间
            // 存在 TOCTOU 窗口（需两个管理员并发反向移动同一对节点才可触达），未加事务隔离——
            // 上轮无守卫直调 manager 时同样存在且无任何防护，本轮为净改善。
            var targetParent = await _organizationUnitRepository.GetAsync(input.ParentId.Value);
            if (targetParent.Code.StartsWith(ou.Code))
            {
                throw new UserFriendlyException(L["OrganizationUnit:CannotMoveIntoOwnSubtree", ou.DisplayName]);
            }
        }

        await _organizationUnitManager.MoveAsync(id, input.ParentId);
    }

    public virtual async Task<PagedResultDto<OrganizationUnitUserDto>> GetMembersAsync(
        Guid id,
        PagedAndSortedResultRequestDto input)
    {
        var ou = await _organizationUnitRepository.GetAsync(id);
        var count = await _organizationUnitRepository.GetMembersCountAsync(ou);
        var users = await _organizationUnitRepository.GetMembersAsync(
            ou,
            input.Sorting,
            input.MaxResultCount,
            input.SkipCount);
        return new PagedResultDto<OrganizationUnitUserDto>(
            count,
            users.Select(x => new OrganizationUnitUserDto
            {
                Id = x.Id,
                UserName = x.UserName,
                Email = x.Email
            }).ToList());
    }

    [Authorize(AbpAdminPermissions.OrganizationUnits.ManageMembers)]
    public virtual async Task AddMembersAsync(Guid id, Guid[] userIds)
    {
        var ou = await _organizationUnitRepository.GetAsync(id);

        // 先批量取回用户（一次查询），再逐个加入（Manager 内含查重），替代逐用户 GetByIdAsync 的 N+1
        var distinctUserIds = userIds.Distinct().ToList();
        var users = await _userQueryRepository.GetListAsync(u => distinctUserIds.Contains(u.Id));

        var usersById = users.ToDictionary(u => u.Id);
        foreach (var userId in distinctUserIds)
        {
            if (!usersById.TryGetValue(userId, out var user))
            {
                // 与原先 GetByIdAsync（找不到抛 EntityNotFound）保持一致的行为
                throw new EntityNotFoundException(typeof(IdentityUser), userId);
            }

            await _userManager.AddToOrganizationUnitAsync(user, ou);
        }
    }

    [Authorize(AbpAdminPermissions.OrganizationUnits.ManageMembers)]
    public virtual async Task RemoveMemberAsync(Guid id, Guid userId)
    {
        var ou = await _organizationUnitRepository.GetAsync(id);
        var user = await _userManager.GetByIdAsync(userId);
        await _userManager.RemoveFromOrganizationUnitAsync(user, ou);
    }

    public virtual async Task<ListResultDto<OrganizationUnitRoleDto>> GetRolesAsync(Guid id)
    {
        var ou = await _organizationUnitRepository.GetAsync(id);
        var roles = await _organizationUnitRepository.GetRolesAsync(ou);
        return new ListResultDto<OrganizationUnitRoleDto>(
            roles.Select(x => new OrganizationUnitRoleDto
            {
                Id = x.Id,
                Name = x.Name
            }).ToList());
    }

    [Authorize(AbpAdminPermissions.OrganizationUnits.ManageRoles)]
    public virtual async Task AddRolesAsync(Guid id, Guid[] roleIds)
    {
        foreach (var roleId in roleIds.Distinct())
        {
            await _organizationUnitManager.AddRoleToOrganizationUnitAsync(roleId, id);
        }
    }

    [Authorize(AbpAdminPermissions.OrganizationUnits.ManageRoles)]
    public virtual async Task RemoveRoleAsync(Guid id, Guid roleId)
    {
        await _organizationUnitManager.RemoveRoleFromOrganizationUnitAsync(roleId, id);
    }

    /// <summary>组织单元 → DTO 的唯一映射出口（原先同一段手工映射重复 3 份）。</summary>
    private static OrganizationUnitDto MapToDto(OrganizationUnit ou)
    {
        return new OrganizationUnitDto
        {
            Id = ou.Id,
            ParentId = ou.ParentId,
            Code = ou.Code,
            DisplayName = ou.DisplayName
        };
    }
}
