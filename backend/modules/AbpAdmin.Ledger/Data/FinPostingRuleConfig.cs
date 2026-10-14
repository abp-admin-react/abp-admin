using AbpAdmin.Ledger.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Ledger.Data;

public class FinPostingRuleConfig : IEntityTypeConfiguration<FinPostingRule>
{
    public void Configure(EntityTypeBuilder<FinPostingRule> b)
    {
        b.ToTable(LedgerConsts.DbTablePrefix + "PostingRules", LedgerConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.BizType).IsRequired().HasMaxLength(LedgerConsts.MaxBizTypeLength);
        b.Property(x => x.Phase).IsRequired().HasMaxLength(LedgerConsts.MaxPhaseLength);
        b.Property(x => x.TemplateJson).IsRequired().HasColumnType("jsonb");
        b.Property(x => x.Description).HasMaxLength(LedgerConsts.MaxSummaryLength);
        b.HasIndex(x => new { x.TenantId, x.BizType, x.Phase, x.Version }).IsUnique().AreNullsDistinct(false);
    }
}
