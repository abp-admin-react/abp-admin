using AbpAdmin.Ledger.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Ledger.Data;

public class FinAccountConfig : IEntityTypeConfiguration<FinAccount>
{
    public void Configure(EntityTypeBuilder<FinAccount> b)
    {
        b.ToTable(LedgerConsts.DbTablePrefix + "Accounts", LedgerConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Code).IsRequired().HasMaxLength(LedgerConsts.MaxCodeLength);
        b.Property(x => x.Name).IsRequired().HasMaxLength(LedgerConsts.MaxNameLength);
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().AreNullsDistinct(false);
    }
}
