using AbpAdmin.Ledger.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Ledger.Data;

public class FinManualAdjustConfig : IEntityTypeConfiguration<FinManualAdjust>
{
    public void Configure(EntityTypeBuilder<FinManualAdjust> b)
    {
        b.ToTable(LedgerConsts.DbTablePrefix + "ManualAdjusts", LedgerConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Amount).HasPrecision(28, 18);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(LedgerConsts.MaxCurrencyLength);
        b.Property(x => x.Reason).HasMaxLength(LedgerConsts.MaxSummaryLength);
        b.Property(x => x.RejectReason).HasMaxLength(LedgerConsts.MaxSummaryLength);
        b.Property(x => x.DebitOwnerId).IsRequired().HasMaxLength(LedgerConsts.MaxOwnerIdLength);
        b.Property(x => x.CreditOwnerId).IsRequired().HasMaxLength(LedgerConsts.MaxOwnerIdLength);
        b.HasIndex(x => new { x.TenantId, x.Status });
    }
}
