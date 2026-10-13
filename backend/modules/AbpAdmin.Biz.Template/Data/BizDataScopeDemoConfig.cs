using AbpAdmin.Biz.Template.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Biz.Template.Data;

/// <summary>
/// 数据权限示例实体表映射（Biz 前缀 + 模块内索引口径对齐框架 DataScopeDemoConfig）。
/// </summary>
public class BizDataScopeDemoConfig : IEntityTypeConfiguration<BizDataScopeDemo>
{
    public void Configure(EntityTypeBuilder<BizDataScopeDemo> b)
    {
        b.ToTable(BizTemplateConsts.DbTablePrefix + "DataScopeDemos", BizTemplateConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Name).IsRequired().HasMaxLength(128);
        b.HasIndex(x => new { x.TenantId, x.OrganizationUnitId });
    }
}
