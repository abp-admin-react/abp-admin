using AbpAdmin.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 租户套餐与套餐菜单实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class TenantPackageConfig : IEntityTypeConfiguration<TenantPackage>
{
    public void Configure(EntityTypeBuilder<TenantPackage> b)
    {
        // 租户套餐：Host 专属，套餐勾选的模板菜单子集（无跨表外键，TemplateMenuId 逻辑引用 AppMenus.Id）
        b.ToTable(AbpAdminConsts.DbTablePrefix + "TenantPackages", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Name).IsRequired().HasMaxLength(TenantPackageConsts.MaxNameLength);
        b.Property(x => x.Remark).HasMaxLength(TenantPackageConsts.MaxRemarkLength);
        b.HasIndex(x => x.Name).IsUnique();
        b.HasMany(x => x.Menus).WithOne().HasForeignKey(x => x.PackageId).IsRequired();
    }
}

public class TenantPackageMenuConfig : IEntityTypeConfiguration<TenantPackageMenu>
{
    public void Configure(EntityTypeBuilder<TenantPackageMenu> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "TenantPackageMenus", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.HasIndex(x => new { x.PackageId, x.TemplateMenuId }).IsUnique();
    }
}
