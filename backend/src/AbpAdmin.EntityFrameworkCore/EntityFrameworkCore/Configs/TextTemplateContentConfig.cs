using AbpAdmin.TextTemplates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 数据库存储的模板内容（TextTemplateContent）实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class TextTemplateContentConfig : IEntityTypeConfiguration<TextTemplateContent>
{
    public void Configure(EntityTypeBuilder<TextTemplateContent> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "TextTemplateContents", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Name).IsRequired().HasMaxLength(128);
        b.Property(x => x.CultureName).HasMaxLength(16);
        b.Property(x => x.Content).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.Name, x.CultureName }).IsUnique();
    }
}
