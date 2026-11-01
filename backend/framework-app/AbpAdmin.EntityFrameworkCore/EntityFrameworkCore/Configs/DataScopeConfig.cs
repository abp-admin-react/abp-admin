using AbpAdmin.DataScopes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 角色数据范围与其组织明细、数据范围演示实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class RoleDataScopeConfig : IEntityTypeConfiguration<RoleDataScope>
{
    public void Configure(EntityTypeBuilder<RoleDataScope> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "RoleDataScopes", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.RoleName).IsRequired().HasMaxLength(256);
        b.HasIndex(x => new { x.TenantId, x.RoleName }).IsUnique();
        b.HasMany(x => x.CustomOrganizationUnits)
            .WithOne()
            .HasForeignKey(x => x.RoleDataScopeId)
            .IsRequired();
    }
}

public class RoleDataScopeOrganizationUnitConfig : IEntityTypeConfiguration<RoleDataScopeOrganizationUnit>
{
    public void Configure(EntityTypeBuilder<RoleDataScopeOrganizationUnit> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "RoleDataScopeOrganizationUnits", AbpAdminConsts.DbSchema);
        b.HasKey(x => new { x.RoleDataScopeId, x.OrganizationUnitId });
    }
}

public class DataScopeDemoConfig : IEntityTypeConfiguration<DataScopeDemo>
{
    public void Configure(EntityTypeBuilder<DataScopeDemo> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "DataScopeDemos", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Name).IsRequired().HasMaxLength(128);
        b.HasIndex(x => new { x.TenantId, x.OrganizationUnitId });
    }
}
