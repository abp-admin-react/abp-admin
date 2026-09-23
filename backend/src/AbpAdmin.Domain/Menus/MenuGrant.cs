using System;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Menus;

/// <summary>
/// 菜单-角色授权（表 AppMenuGrants），对应 Admin.NET 的 SysRoleMenu。
/// 按 ABP PermissionGrants 的 Provider 模式建（ProviderName=R + ProviderKey=角色名），
/// 将来扩用户级授权时加 ProviderName=U 即可。租户隔离跟随菜单实体（TenantId 冗余存储便于唯一索引与查询）。
/// </summary>
public class MenuGrant : Entity<Guid>, IMultiTenant
{
    public virtual Guid MenuId { get; protected set; }

    public virtual Guid? TenantId { get; protected set; }

    /// <summary>授权提供者：R=角色（预留 U=用户）。</summary>
    public virtual string ProviderName { get; protected set; } = default!;

    /// <summary>角色名（ProviderName=R 时）。</summary>
    public virtual string ProviderKey { get; protected set; } = default!;

    protected MenuGrant()
    {
    }

    public MenuGrant(Guid id, Guid menuId, Guid? tenantId, string providerName, string providerKey)
        : base(id)
    {
        MenuId = menuId;
        TenantId = tenantId;
        ProviderName = providerName;
        ProviderKey = providerKey;
    }
}
