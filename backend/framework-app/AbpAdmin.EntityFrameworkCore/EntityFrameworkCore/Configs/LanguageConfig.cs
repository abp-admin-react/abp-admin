using AbpAdmin.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 语言与语言文本（数据库驱动本地化）实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class LanguageConfig : IEntityTypeConfiguration<Language>
{
    public void Configure(EntityTypeBuilder<Language> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "Languages", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.CultureName).IsRequired().HasMaxLength(LanguageConsts.MaxCultureNameLength);
        b.Property(x => x.UiCultureName).IsRequired().HasMaxLength(LanguageConsts.MaxUiCultureNameLength);
        b.Property(x => x.DisplayName).IsRequired().HasMaxLength(LanguageConsts.MaxDisplayNameLength);
        b.Property(x => x.FlagIcon).HasMaxLength(LanguageConsts.MaxFlagIconLength);
        b.HasIndex(x => x.CultureName).IsUnique();
    }
}

public class LanguageTextConfig : IEntityTypeConfiguration<LanguageText>
{
    public void Configure(EntityTypeBuilder<LanguageText> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "LanguageTexts", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.ResourceName).IsRequired().HasMaxLength(LanguageTextConsts.MaxResourceNameLength);
        b.Property(x => x.CultureName).IsRequired().HasMaxLength(LanguageTextConsts.MaxCultureNameLength);
        b.Property(x => x.Name).IsRequired().HasMaxLength(LanguageTextConsts.MaxNameLength);
        b.Property(x => x.Value).IsRequired().HasMaxLength(LanguageTextConsts.MaxValueLength);
        b.HasIndex(x => new { x.TenantId, x.ResourceName, x.CultureName, x.Name }).IsUnique();
    }
}
