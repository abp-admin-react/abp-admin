using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Menus;

/// <summary>
/// 动态菜单节点（表 AppMenus）。设计借鉴 Admin.NET 的 SysMenu：
/// 目录/菜单两类节点同一棵树（按钮不入库，走 ABP 权限体系）。
/// Host 侧（TenantId=null）的树是全局模板；租户首次访问时由 MenuManager 懒拷贝一份后自由维护。
/// 系统级配置，不实现 IHasDataScope。
/// </summary>
public class Menu : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>父节点；null 表示顶级。</summary>
    public virtual Guid? ParentId { get; protected set; }

    public virtual MenuTypeEnum Type { get; protected set; }

    /// <summary>默认显示名（locale key 缺失时的兜底文案）。</summary>
    public virtual string Title { get; protected set; } = default!;

    /// <summary>菜单国际化 key 尾段（如 identity），前端拼 menu.{parent}.{name} 查语言包；可空。</summary>
    public virtual string? Name { get; protected set; }

    /// <summary>前端路由地址，须存在于前端路由注册表内；目录节点也记录（如 /administration）。</summary>
    public virtual string? Path { get; protected set; }

    public virtual string? Icon { get; protected set; }

    public virtual int OrderNo { get; protected set; }

    /// <summary>隐藏但保留授权（对应前端 hideInMenu：不出现在菜单，但路由可达，如详情页）。</summary>
    public virtual bool IsHide { get; protected set; }

    public virtual bool IsEnabled { get; protected set; }

    /// <summary>绑定的 ABP 权限名（可选）。混合授权：绑定了权限则要求权限已授予。</summary>
    public virtual string? PermissionName { get; protected set; }

    public virtual string? Remark { get; set; }

    protected Menu()
    {
    }

    public Menu(
        Guid id,
        Guid? tenantId,
        Guid? parentId,
        MenuTypeEnum type,
        string title,
        string? name = null,
        string? path = null,
        string? icon = null,
        int orderNo = MenuConsts.DefaultOrderNo,
        bool isHide = false,
        bool isEnabled = true,
        string? permissionName = null)
        : base(id)
    {
        TenantId = tenantId;
        ParentId = parentId;
        Type = type;
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MenuConsts.MaxTitleLength);
        SetName(name);
        SetPath(path);
        SetIcon(icon);
        SetPermissionName(permissionName);
        OrderNo = orderNo;
        IsHide = isHide;
        IsEnabled = isEnabled;
    }

    public virtual void SetParent(Guid? parentId)
    {
        ParentId = parentId;
    }

    public virtual void SetType(MenuTypeEnum type)
    {
        Type = type;
    }

    public virtual void SetTitle(string title)
    {
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), MenuConsts.MaxTitleLength);
    }

    public virtual void SetName(string? name)
    {
        Name = name is null
            ? null
            : Check.NotNullOrWhiteSpace(name, nameof(name), MenuConsts.MaxNameLength);
    }

    public virtual void SetPath(string? path)
    {
        Path = string.IsNullOrWhiteSpace(path)
            ? null
            : Check.NotNullOrWhiteSpace(path, nameof(path), MenuConsts.MaxPathLength);
    }

    public virtual void SetIcon(string? icon)
    {
        Icon = string.IsNullOrWhiteSpace(icon)
            ? null
            : Check.NotNullOrWhiteSpace(icon, nameof(icon), MenuConsts.MaxIconLength);
    }

    public virtual void SetOrderNo(int orderNo)
    {
        OrderNo = orderNo;
    }

    public virtual void Hide()
    {
        IsHide = true;
    }

    public virtual void Show()
    {
        IsHide = false;
    }

    /// <summary>按目标可见性设置隐藏状态（隐藏 = 不出现在侧边菜单但路由可达），调用方免写 if-else 分支。</summary>
    public virtual void SetVisible(bool isVisible)
    {
        IsHide = !isVisible;
    }

    public virtual void Enable()
    {
        IsEnabled = true;
    }

    public virtual void Disable()
    {
        IsEnabled = false;
    }

    /// <summary>按目标状态设置启用（停用后整棵子树不可见），调用方免写 if-else 分支。</summary>
    public virtual void SetEnabled(bool isEnabled)
    {
        IsEnabled = isEnabled;
    }

    public virtual void SetPermissionName(string? permissionName)
    {
        PermissionName = string.IsNullOrWhiteSpace(permissionName)
            ? null
            : Check.NotNullOrWhiteSpace(permissionName, nameof(permissionName), MenuConsts.MaxPermissionNameLength);
    }
}
