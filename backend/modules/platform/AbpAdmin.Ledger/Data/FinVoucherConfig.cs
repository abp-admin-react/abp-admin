using AbpAdmin.Ledger.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Ledger.Data;

public class FinVoucherConfig : IEntityTypeConfiguration<FinVoucher>
{
    public void Configure(EntityTypeBuilder<FinVoucher> b)
    {
        b.ToTable(LedgerConsts.DbTablePrefix + "Vouchers", LedgerConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.BizType).IsRequired().HasMaxLength(LedgerConsts.MaxBizTypeLength);
        b.Property(x => x.BizRefId).IsRequired().HasMaxLength(LedgerConsts.MaxBizRefIdLength);
        b.Property(x => x.Phase).IsRequired().HasMaxLength(LedgerConsts.MaxPhaseLength);
        b.Property(x => x.BatchKey).IsRequired().HasMaxLength(LedgerConsts.MaxBatchKeyLength);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(LedgerConsts.MaxCurrencyLength);
        b.Property(x => x.Amount).HasPrecision(28, 18);
        b.Property(x => x.Summary).HasMaxLength(LedgerConsts.MaxSummaryLength);
        b.Property(x => x.Fingerprint).IsRequired().HasMaxLength(64);
        // 终审：UK (TenantId, BizType, BizRefId, Phase, BatchKey)
        b.HasIndex(x => new { x.TenantId, x.BizType, x.BizRefId, x.Phase, x.BatchKey }).IsUnique().AreNullsDistinct(false);
        b.HasIndex(x => new { x.TenantId, x.AgentId });
        b.HasIndex(x => new { x.TenantId, x.IsDemo });
        b.HasIndex(x => x.ReversalOf);
        b.HasIndex(x => new { x.TenantId, x.PostedTime });
    }
}
