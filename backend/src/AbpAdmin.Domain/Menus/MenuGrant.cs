using System;
using Volo.Abp;
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

    /// <summary>
    /// 角色重命名时同步改写授权键（ProviderName=R 时 ProviderKey 即角色名）。
    /// 只应由 <c>RoleRenamedCascadeHandler</c> 调用——它是 IdentityRoleNameChangedEto 的级联点，
    /// 任何其它路径改键都会造成授权与角色失配（受控菜单对角色成员静默消失）。
    /// </summary>
    public void ChangeProviderKey(string providerKey)
    {
        ProviderKey = Check.NotNullOrWhiteSpace(providerKey, nameof(providerKey), maxLength: 256);
    }
}
