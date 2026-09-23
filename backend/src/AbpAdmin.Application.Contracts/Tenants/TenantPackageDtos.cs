using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Tenants;

public class TenantPackageDto : EntityDto<Guid>
{
    public string Name { get; set; } = default!;

    public string? Remark { get; set; }

    public int MenuCount { get; set; }
}

public class TenantPackageCreateDto
{
    [Required]
    [StringLength(TenantPackageConsts.MaxNameLength)]
    public string Name { get; set; } = default!;

    [StringLength(TenantPackageConsts.MaxRemarkLength)]
    public string? Remark { get; set; }
}

public class TenantPackageUpdateDto : TenantPackageCreateDto
{
}

public class TenantPackageListInput : PagedAndSortedResultRequestDto
{
    public string? Filter { get; set; }
}

public class TenantPackageMenuSelectionDto
{
    /// <summary>Host 模板树（管理端全量，含停用/隐藏），结构同菜单管理的 MenuTreeDto。</summary>
    public List<Menus.MenuTreeDto> Tree { get; set; } = new();

    /// <summary>已勾选的模板菜单 Id。</summary>
    public List<Guid> CheckedMenuIds { get; set; } = new();
}

public class UpdateTenantPackageMenusDto
{
    public List<Guid> MenuIds { get; set; } = new();
}

/// <summary>把套餐应用到租户：租户菜单树重置为"套餐过滤后的模板拷贝"（破坏性）。</summary>
public class ApplyTenantPackageDto
{
    /// <summary>null = 全量重置（不限套餐）。</summary>
    public Guid? PackageId { get; set; }
}
