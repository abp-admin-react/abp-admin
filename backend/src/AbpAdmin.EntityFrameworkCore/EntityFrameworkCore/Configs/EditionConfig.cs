using AbpAdmin.Editions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 版本（Edition）实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class EditionConfig : IEntityTypeConfiguration<Edition>
{
    public void Configure(EntityTypeBuilder<Edition> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "Editions", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(EditionConsts.MaxDisplayNameLength);
        b.HasIndex(x => x.DisplayName);
    }
}
