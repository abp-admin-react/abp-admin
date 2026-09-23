using AbpAdmin.Menus;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 动态菜单与菜单授权实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
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

public class MenuGrantConfig : IEntityTypeConfiguration<MenuGrant>
{
    public void Configure(EntityTypeBuilder<MenuGrant> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "MenuGrants", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.ProviderName).IsRequired().HasMaxLength(MenuConsts.MaxProviderKeyLength);
        b.Property(x => x.ProviderKey).IsRequired().HasMaxLength(MenuConsts.MaxProviderKeyLength);
        b.HasIndex(x => new { x.MenuId, x.ProviderName, x.ProviderKey }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ProviderName, x.ProviderKey });
        // 外键级联：授权行随菜单行生灭（应用层此前手工删除是唯一防线，这里补上 DB 层防线——
        // 任何绕过应用层的删除都不再留下孤儿授权行）。菜单删除是硬删除，级联可生效。
        b.HasOne<Menu>()
            .WithMany()
            .HasForeignKey(x => x.MenuId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
