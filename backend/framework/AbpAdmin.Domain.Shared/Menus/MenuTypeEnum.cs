using System.ComponentModel;

namespace AbpAdmin.Menus;

/// <summary>
/// 菜单节点类型。借鉴 Admin.NET 的 SysMenu.Type，但去掉"按钮"类型——
/// 按钮/接口授权统一走 ABP 权限体系（AbpAdminPermissions + PermissionGrants），
/// 避免菜单表与权限表双体系维护同一批按钮。
/// 以 Enum 结尾的公开枚举会被种子同步为静态数据字典（字典编码 MenuType）。
/// </summary>
[Description("菜单类型")]
public enum MenuTypeEnum
{
    /// <summary>目录（分组节点，无页面，仅承载子菜单）</summary>
    [Description("目录")]
    Catalog = 1,

    /// <summary>菜单（叶子页面，Path 指向前端路由注册表中的地址）</summary>
    [Description("菜单")]
    Menu = 2
}
