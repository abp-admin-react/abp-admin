using AbpAdmin.Ledger.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Ledger.Data;

public class FinEntryConfig : IEntityTypeConfiguration<FinEntry>
{
    public void Configure(EntityTypeBuilder<FinEntry> b)
    {
        b.ToTable(LedgerConsts.DbTablePrefix + "Entries", LedgerConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Debit).HasPrecision(28, 18);
        b.Property(x => x.Credit).HasPrecision(28, 18);
        b.Property(x => x.BalanceAfter).HasPrecision(28, 18);
        b.Property(x => x.Dimensions).HasColumnType("jsonb");
        b.HasIndex(x => new { x.VoucherId, x.Seq }).IsUnique();
        b.HasIndex(x => x.SubAccountId);
    }
}
