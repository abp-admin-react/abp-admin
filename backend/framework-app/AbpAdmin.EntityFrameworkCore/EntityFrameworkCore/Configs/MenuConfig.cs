using AbpAdmin.Menus;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 动态菜单实体映射。
/// </summary>
public class MenuConfig : IEntityTypeConfiguration<Menu>
{
    public void Configure(EntityTypeBuilder<Menu> b)
    {
        // 动态菜单（设计借鉴 Admin.NET SysMenu/SysRoleMenu）。
        // Host 侧（TenantId=null）是全局模板；(TenantId, Path) 唯一防租户懒拷贝并发重复。
        b.ToTable(AbpAdminConsts.DbTablePrefix + "Menus", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Title).IsRequired().HasMaxLength(MenuConsts.MaxTitleLength);
        b.Property(x => x.Name).HasMaxLength(MenuConsts.MaxNameLength);
        b.Property(x => x.Path).HasMaxLength(MenuConsts.MaxPathLength);
        b.Property(x => x.Icon).HasMaxLength(MenuConsts.MaxIconLength);
        b.Property(x => x.PermissionName).HasMaxLength(MenuConsts.MaxPermissionNameLength);
        b.Property(x => x.Remark).HasMaxLength(MenuConsts.MaxRemarkLength);
        b.HasIndex(x => new { x.TenantId, x.Path }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ParentId });
    }
}
