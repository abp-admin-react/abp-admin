using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Menus;

public interface IMenuAppService : IApplicationService
{
    /// <summary>管理端全量树（含停用/隐藏节点与已分配角色），供菜单管理页展示。</summary>
    Task<ListResultDto<MenuTreeDto>> GetTreeAsync();

    Task<MenuDto> GetAsync(Guid id);

    Task<MenuDto> CreateAsync(MenuCreateDto input);

    Task<MenuDto> UpdateAsync(Guid id, MenuUpdateDto input);

    Task DeleteAsync(Guid id);

    /// <summary>节点当前已分配的角色名列表。</summary>
    Task<ListResultDto<string>> GetRoleGrantsAsync(Guid menuId);

    /// <summary>全量覆盖节点的角色分配（勾选 id 含父节点，Admin.NET 同款语义）。</summary>
    Task UpdateRoleGrantsAsync(Guid menuId, UpdateMenuGrantsDto input);

    /// <summary>可绑定的 ABP 权限树（平铺 name/displayName/parentName，前端组树）。</summary>
    Task<ListResultDto<PermissionOptionDto>> GetPermissionOptionsAsync();
}

public interface IMyMenuAppService : IApplicationService
{
    /// <summary>当前登录用户可见的菜单树（混合授权过滤 + 空目录折叠）。</summary>
    Task<ListResultDto<MyMenuItemDto>> GetAsync();
}

public class MenuDto : EntityDto<Guid>
{
    public Guid? TenantId { get; set; }

    public Guid? ParentId { get; set; }

    public MenuTypeEnum Type { get; set; }

    /// <summary>默认显示名。</summary>
    public string Title { get; set; } = default!;

    /// <summary>国际化 key 尾段，可空。</summary>
    public string? Name { get; set; }

    public string? Path { get; set; }

    public string? Icon { get; set; }

    public int OrderNo { get; set; }

    public bool IsHide { get; set; }

    public bool IsEnabled { get; set; }

    public string? PermissionName { get; set; }

    public string? Remark { get; set; }

    /// <summary>
    /// 并发戳（乐观并发）：编辑表单携带加载时的值回传，服务端与库内当前值不一致即 409。
    /// 实体上有该列（FullAuditedAggregateRoot），此前未下发导致编辑冲突被静默覆盖。
    /// </summary>
    public string? ConcurrencyStamp { get; set; }
}

public class MenuTreeDto : MenuDto
{
    public List<string> GrantedRoles { get; set; } = new();

    public List<MenuTreeDto> Children { get; set; } = new();
}

public class MenuCreateDto
{
    public Guid? ParentId { get; set; }

    public MenuTypeEnum Type { get; set; }

    [Required]
    [StringLength(MenuConsts.MaxTitleLength)]
    public string Title { get; set; } = default!;

    [StringLength(MenuConsts.MaxNameLength)]
    public string? Name { get; set; }

    [StringLength(MenuConsts.MaxPathLength)]
    public string? Path { get; set; }

    [StringLength(MenuConsts.MaxIconLength)]
    public string? Icon { get; set; }

    /// <summary>排序值（前端限制 0-9999）。</summary>
    [Range(0, 9999)]
    public int OrderNo { get; set; } = MenuConsts.DefaultOrderNo;

    public bool IsHide { get; set; }

    public bool IsEnabled { get; set; } = true;

    [StringLength(MenuConsts.MaxPermissionNameLength)]
    public string? PermissionName { get; set; }

    [StringLength(MenuConsts.MaxRemarkLength)]
    public string? Remark { get; set; }
}

public class MenuUpdateDto : MenuCreateDto
{
    /// <summary>并发戳：传了就校验（与库内当前值不一致抛 AbpDbConcurrencyException→409），不传跳过（兼容旧调用方）。</summary>
    public string? ConcurrencyStamp { get; set; }
}

public class UpdateMenuGrantsDto
{
    public List<string> RoleNames { get; set; } = new();
}

public class PermissionOptionDto
{
    public string Name { get; set; } = default!;

    public string DisplayName { get; set; } = default!;

    public string? ParentName { get; set; }
}

/// <summary>my-menu 返回项：title 兜底文案、name 国际化尾段、icon 图标名，前端转 ProLayout MenuDataItem。</summary>
public class MyMenuItemDto
{
    public string Title { get; set; } = default!;

    public string? Name { get; set; }

    public string? Path { get; set; }

    public string? Icon { get; set; }

    public List<MyMenuItemDto> Children { get; set; } = new();
}
