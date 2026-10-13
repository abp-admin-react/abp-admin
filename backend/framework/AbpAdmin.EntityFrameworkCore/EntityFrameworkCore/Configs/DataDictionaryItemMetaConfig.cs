using AbpAdmin.DataDictionaries;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 字典项展示元数据（旁挂 EasyAbp 数据字典模块表）实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class DataDictionaryItemMetaConfig : IEntityTypeConfiguration<DataDictionaryItemMeta>
{
    public void Configure(EntityTypeBuilder<DataDictionaryItemMeta> b)
    {
        // T3.4：字典项展示元数据（旁挂模块表，逻辑关联不建外键）。
        b.ToTable(AbpAdminConsts.DbTablePrefix + "DataDictionaryItemMetas", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.DictionaryCode).IsRequired().HasMaxLength(DataDictionaryItemMetaConsts.MaxDictionaryCodeLength);
        b.Property(x => x.ItemCode).IsRequired().HasMaxLength(DataDictionaryItemMetaConsts.MaxItemCodeLength);
        b.Property(x => x.TagType).HasMaxLength(DataDictionaryItemMetaConsts.MaxTagTypeLength);
        b.HasIndex(x => new { x.TenantId, x.DictionaryCode, x.ItemCode }).IsUnique();
    }
}
