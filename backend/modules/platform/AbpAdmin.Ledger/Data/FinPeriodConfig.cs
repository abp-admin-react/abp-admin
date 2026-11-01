using AbpAdmin.Ledger.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Ledger.Data;

public class FinPeriodConfig : IEntityTypeConfiguration<FinPeriod>
{
    public void Configure(EntityTypeBuilder<FinPeriod> b)
    {
        b.ToTable(LedgerConsts.DbTablePrefix + "Periods", LedgerConsts.DbSchema);
        b.ConfigureByConvention();
        b.HasIndex(x => new { x.TenantId, x.Year, x.Month }).IsUnique().AreNullsDistinct(false);
    }
}
