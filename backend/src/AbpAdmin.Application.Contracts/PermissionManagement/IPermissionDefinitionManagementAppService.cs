using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.PermissionManagement;

/// <summary>
/// 权限定义的运行时管理（Host 专属）。把权限定义从"编译期写死"升级为可运营能力：
/// 落库到 ABP 既有的 PermissionGroupDefinitionRecord / PermissionDefinitionRecord 表，
/// 由框架 DynamicPermissionDefinitionStore 读取；写操作后删除公共 stamp 键触发全实例刷新。
/// 静态（代码内）定义不受影响，运行时定义不得与静态同名遮蔽。
/// </summary>
public interface IPermissionDefinitionManagementAppService : IApplicationService
{
    Task<PagedResultDto<PermissionGroupRecordDto>> GetGroupsAsync(GetPermissionGroupListInput input);

    Task<PagedResultDto<PermissionDefinitionRecordDto>> GetDefinitionsAsync(GetPermissionDefinitionListInput input);

    Task<PermissionGroupRecordDto> CreateGroupAsync(CreatePermissionGroupInput input);

    Task<PermissionGroupRecordDto> UpdateGroupAsync(Guid id, UpdatePermissionGroupInput input);

    Task DeleteGroupAsync(Guid id);

    Task<PermissionDefinitionRecordDto> CreateDefinitionAsync(CreatePermissionDefinitionInput input);

    Task<PermissionDefinitionRecordDto> UpdateDefinitionAsync(Guid id, UpdatePermissionDefinitionInput input);

    Task DeleteDefinitionAsync(Guid id);
}

public class GetPermissionGroupListInput : PagedResultRequestDto
{
    /// <summary>按组名/显示名模糊过滤（可选）。</summary>
    public string? Filter { get; set; }
}

public class GetPermissionDefinitionListInput : PagedResultRequestDto
{
    /// <summary>按所属组名精确过滤（可选）。</summary>
    public string? GroupName { get; set; }

    /// <summary>按权限名/显示名模糊过滤（可选）。</summary>
    public string? Filter { get; set; }
}

public class PermissionGroupRecordDto : EntityDto<Guid>
{
    public string Name { get; set; } = default!;

    /// <summary>落库的本地化串（"L:资源:键" 格式，原样返回）。</summary>
    public string DisplayName { get; set; } = default!;

    /// <summary>按当前请求文化解析后的显示名（解析失败回退原串）。</summary>
    public string DisplayNameLocalized { get; set; } = default!;
}

public class PermissionDefinitionRecordDto : EntityDto<Guid>
{
    public string? GroupName { get; set; }

    public string Name { get; set; } = default!;

    public string? ParentName { get; set; }

    public string DisplayName { get; set; } = default!;

    public string DisplayNameLocalized { get; set; } = default!;

    public bool IsEnabled { get; set; }

    /// <summary>multiTenancySide 数值（0=Both,1=Host,2=Tenant）。</summary>
    public byte MultiTenancySide { get; set; }
}

public class CreatePermissionGroupInput
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = default!;

    /// <summary>显示名。以 "L:资源:键" 传入可本地化，否则原样展示。</summary>
    [Required]
    [StringLength(256)]
    public string DisplayName { get; set; } = default!;
}

public class UpdatePermissionGroupInput
{
    [Required]
    [StringLength(256)]
    public string DisplayName { get; set; } = default!;
}

public class CreatePermissionDefinitionInput
{
    /// <summary>所属组名（运行时组须已存在；静态组名也允许——定义可挂到代码定义的组下）。</summary>
    [Required]
    [StringLength(128)]
    public string GroupName { get; set; } = default!;

    /// <summary>权限全名（如 MyBiz.Orders.Approve），与静态定义同名会被拒绝（防遮蔽）。</summary>
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = default!;

    /// <summary>父权限名（可选，须已存在于静态或动态定义）。</summary>
    [StringLength(128)]
    public string? ParentName { get; set; }

    [Required]
    [StringLength(256)]
    public string DisplayName { get; set; } = default!;

    public bool IsEnabled { get; set; } = true;
}

public class UpdatePermissionDefinitionInput
{
    [Required]
    [StringLength(256)]
    public string DisplayName { get; set; } = default!;

    public bool IsEnabled { get; set; } = true;
}
