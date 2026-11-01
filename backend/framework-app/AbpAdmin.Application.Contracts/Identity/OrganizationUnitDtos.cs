using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

public interface IOrganizationUnitAppService : IApplicationService
{
    Task<List<OrganizationUnitDto>> GetListAsync();

    Task<OrganizationUnitDto> CreateAsync(CreateOrganizationUnitDto input);

    Task<OrganizationUnitDto> UpdateAsync(Guid id, UpdateOrganizationUnitDto input);

    Task DeleteAsync(Guid id);

    /// <summary>
    /// 移动组织单元。ParentId 为空＝移到根级（路由 /move/{id}，父子关系走请求体，
    /// 可空语义不再受「路由段无法表达 null」的限制）。
    /// </summary>
    Task MoveAsync(Guid id, MoveOrganizationUnitInput input);

    Task<PagedResultDto<OrganizationUnitUserDto>> GetMembersAsync(Guid id, PagedAndSortedResultRequestDto input);

    Task AddMembersAsync(Guid id, Guid[] userIds);

    Task RemoveMemberAsync(Guid id, Guid userId);

    Task<ListResultDto<OrganizationUnitRoleDto>> GetRolesAsync(Guid id);

    Task AddRolesAsync(Guid id, Guid[] roleIds);

    Task RemoveRoleAsync(Guid id, Guid roleId);
}

public class OrganizationUnitDto : EntityDto<Guid>
{
    public Guid? ParentId { get; set; }

    public string Code { get; set; } = default!;

    public string DisplayName { get; set; } = default!;
}

public class CreateOrganizationUnitDto
{
    public Guid? ParentId { get; set; }

    [Required]
    [StringLength(128)] // 与 Volo.Abp.Identity.OrganizationUnitConsts.MaxDisplayNameLength 保持一致（非 const，特性内不能引用）
    public string DisplayName { get; set; } = default!;
}

public class UpdateOrganizationUnitDto
{
    [Required]
    [StringLength(128)] // 同上
    public string DisplayName { get; set; } = default!;
}

/// <summary>
/// 移动组织单元入参。ParentId 为空＝提升为根级；不为空时服务端做防环校验
/// （目标父不得在本节点子树内，OU Code 前缀判定）。
/// </summary>
public class MoveOrganizationUnitInput
{
    public Guid? ParentId { get; set; }
}

public class OrganizationUnitUserDto : EntityDto<Guid>
{
    public string UserName { get; set; } = default!;

    public string? Email { get; set; }
}

public class OrganizationUnitRoleDto : EntityDto<Guid>
{
    public string Name { get; set; } = default!;
}
