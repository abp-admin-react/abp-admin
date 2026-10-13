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
    Task<ListResultDto<string>> GetMenuRoleGrantsAsync(Guid menuId);

    /// <summary>全量覆盖节点的角色分配（勾选 id 含父节点，Admin.NET 同款语义）。</summary>
    Task UpdateMenuRoleGrantsAsync(Guid menuId, UpdateMenuGrantsDto input);

    /// <summary>可绑定的 ABP 权限树（平铺 name/displayName/parentName，前端组树）。</summary>
    Task<ListResultDto<PermissionOptionDto>> GetPermissionOptionsAsync();

    /// <summary>
    /// 角色侧菜单授权视图：平铺全量菜单 + 该角色已授权标记（IsGranted）+
    /// 各节点是否受角色勾选控制（IsControlled，存在任一角色的授权记录）。
    /// 供角色页「菜单权限」弹窗组树三态展示（勾选=显式授权；未受控=公开）。
    /// </summary>
    Task<ListResultDto<RoleMenuGrantItemDto>> GetRoleMenuGrantsAsync(Guid roleId);

    /// <summary>
    /// 以勾选集为该角色的菜单授权全集做差集更新：勾上=插入授权、勾掉=删除该角色授权；
    /// 未受控（无任何角色授权）的菜单不受影响、其它角色的授权不受影响。
    /// </summary>
    Task UpdateRoleMenuGrantsAsync(Guid roleId, UpdateRoleMenuGrantsDto input);
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

/// <summary>角色侧菜单授权视图项（平铺，前端组树）。</summary>
public class RoleMenuGrantItemDto
{
    public Guid Id { get; set; }

    public Guid? ParentId { get; set; }

    public MenuTypeEnum Type { get; set; }

    public string Title { get; set; } = default!;

    public int OrderNo { get; set; }

    public bool IsEnabled { get; set; }

    /// <summary>该角色在此菜单上已有显式授权。</summary>
    public bool IsGranted { get; set; }

    /// <summary>该菜单是否受角色勾选控制（存在任一角色的授权记录）；false = 公开（对所有用户可见，满足权限时）。</summary>
    public bool IsControlled { get; set; }

    /// <summary>菜单是否隐藏（隐藏节点自身不出现在用户侧边栏，子节点上浮）——勾选树如实透出，避免「勾了却不生效」的误导。</summary>
    public bool IsHide { get; set; }
}

public class UpdateRoleMenuGrantsDto
{
    /// <summary>勾选集（该角色授权全集）。Required 拒绝显式 null（模型绑定会覆盖 = new() 初始化器，无标注时落到服务端变 NRE→500）。</summary>
    [Required]
    public List<Guid> MenuIds { get; set; } = new();
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
