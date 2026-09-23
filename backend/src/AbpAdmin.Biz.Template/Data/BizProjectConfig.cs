using AbpAdmin.Biz.Template.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// BizProject 表映射（与框架 Configs/ 目录同一风格，但随模块自持）。
/// </summary>
public class BizProjectConfig : IEntityTypeConfiguration<BizProject>
{
    public void Configure(EntityTypeBuilder<BizProject> b)
    {
        b.ToTable(BizTemplateConsts.DbTablePrefix + "Projects", BizTemplateConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Name).IsRequired().HasMaxLength(BizTemplateConsts.MaxNameLength);
        b.Property(x => x.Description).HasMaxLength(BizTemplateConsts.MaxDescriptionLength);
        b.HasIndex(x => x.IsActive);
    }
}
