using System;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Identity;
using Volo.Abp.TenantManagement;

namespace AbpAdmin.Grids;

/// <summary>将扩展属性投影为数据库可比较的列，列表可在分页前组合筛选。</summary>
public interface IAdminGridProjections
{
    Task<IQueryable<RoleGridRecord>> RolesAsync();
    Task<IQueryable<TenantGridRecord>> TenantsAsync(bool canReadEditionNames);
}

public sealed class RoleGridRecord
{
    public IdentityRole Role { get; init; } = default!;
    public string? Remark { get; init; }
}

public sealed class TenantGridRecord
{
    public Tenant Tenant { get; init; } = default!;
    public string? Edition { get; init; }
    public int ActivationState { get; init; }
    public DateTime? EditionEnd { get; init; }
}
