using AbpAdmin.Ledger.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Ledger.Data;

public class FinBalanceProjectionConfig : IEntityTypeConfiguration<FinBalanceProjection>
{
    public void Configure(EntityTypeBuilder<FinBalanceProjection> b)
    {
        b.ToTable(LedgerConsts.DbTablePrefix + "BalanceProjections", LedgerConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Balance).HasPrecision(28, 18);
        b.HasIndex(x => new { x.TenantId, x.SubAccountId }).IsUnique().AreNullsDistinct(false);
    }
}
