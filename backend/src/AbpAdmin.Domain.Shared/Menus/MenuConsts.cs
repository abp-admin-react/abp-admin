namespace AbpAdmin.Menus;

/// <summary>
/// 菜单管理字段长度常量（表 AppMenus / AppMenuGrants）。EF 配置与实体校验共用同一份，避免漂移。
/// </summary>
public static class MenuConsts
{
    public const int MaxTitleLength = 64;

    /// <summary>菜单国际化 key 尾段（如 identity），前端拼 menu.{parent}.{name} 查语言包。</summary>
    public const int MaxNameLength = 64;

    /// <summary>前端路由地址，须存在于前端路由注册表内。</summary>
    public const int MaxPathLength = 128;

    public const int MaxIconLength = 64;

    /// <summary>绑定的 ABP 权限名（如 AbpAdmin.AuditLogs），可选。</summary>
    public const int MaxPermissionNameLength = 256;

    public const int MaxRemarkLength = 256;

    public const int MaxProviderKeyLength = 64;

    /// <summary>同一父级下建议的默认排序值（Admin.NET 惯例：100 起，数字小的在前）。</summary>
    public const int DefaultOrderNo = 100;

    /// <summary>
    /// 菜单角色授权的 Provider 名（对应 ABP PermissionGrants 的 "R" 约定，预留 "U"=用户）。
    /// 读写两侧共用同一出处，避免字面量漂移。
    /// </summary>
    public const string RoleProviderName = "R";

    /// <summary>
    /// 沿父链向上遍历的最大步数（防环守卫）。菜单树实际深度 ≤ 4，64 已极宽松；
    /// 所有「沿 ParentId 上行」的遍历（MenuTreeWalker 及其消费方）统一引用本常量，避免魔法数散落。
    /// </summary>
    public const int MaxTreeDepth = 64;
}
