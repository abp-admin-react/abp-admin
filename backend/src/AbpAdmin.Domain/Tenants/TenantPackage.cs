using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.Guids;

namespace AbpAdmin.Tenants;

/// <summary>
/// 租户套餐菜单项（表 AppTenantPackageMenus）：套餐勾选的 Host 模板菜单节点。
/// TemplateMenuId 逻辑引用 Host 侧 AppMenus.Id（不建跨表外键）。
/// </summary>
public class TenantPackageMenu : Entity<Guid>
{
    public virtual Guid PackageId { get; protected set; }

    /// <summary>Host 模板菜单节点 Id。</summary>
    public virtual Guid TemplateMenuId { get; protected set; }

    protected TenantPackageMenu()
    {
    }

    public TenantPackageMenu(Guid id, Guid packageId, Guid templateMenuId)
        : base(id)
    {
        PackageId = packageId;
        TemplateMenuId = templateMenuId;
    }
}

/// <summary>
/// 租户套餐（表 AppTenantPackages）。借鉴芋道"租户套餐"与 Admin.NET"租户菜单白名单"：
/// Host 把全局菜单模板的子集打包成套餐，创建租户时按套餐过滤拷贝菜单树，
/// 实现"不同租户拿到不同的菜单结构"。仅 Host 侧数据（不实现 IMultiTenant，
/// 实体恒定属于 Host，与 ABP Edition 的 host-only 存法一致）。
/// </summary>
public class TenantPackage : FullAuditedAggregateRoot<Guid>
{
    /// <summary>套餐名，全局唯一。</summary>
    public virtual string Name { get; protected set; } = default!;

    public virtual string? Remark { get; set; }

    public virtual IReadOnlyList<TenantPackageMenu> Menus => _menus;

    private readonly List<TenantPackageMenu> _menus = new();

    protected TenantPackage()
    {
    }

    public TenantPackage(Guid id, string name, string? remark = null)
        : base(id)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), TenantPackageConsts.MaxNameLength);
        Remark = remark;
    }

    public virtual void SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), TenantPackageConsts.MaxNameLength);
    }

    /// <summary>全量替换套餐勾选的模板菜单。</summary>
    public virtual void SetMenus(IEnumerable<Guid> templateMenuIds, IGuidGenerator guidGenerator)
    {
        Check.NotNull(guidGenerator, nameof(guidGenerator));

        _menus.RemoveAll(x => true);
        foreach (var menuId in templateMenuIds.Distinct())
        {
            _menus.Add(new TenantPackageMenu(guidGenerator.Create(), Id, menuId));
        }
    }
}
