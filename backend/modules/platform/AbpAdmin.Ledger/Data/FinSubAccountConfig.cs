using AbpAdmin.Ledger.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Ledger.Data;

public class FinSubAccountConfig : IEntityTypeConfiguration<FinSubAccount>
{
    public void Configure(EntityTypeBuilder<FinSubAccount> b)
    {
        b.ToTable(LedgerConsts.DbTablePrefix + "SubAccounts", LedgerConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.OwnerId).IsRequired().HasMaxLength(LedgerConsts.MaxOwnerIdLength);
        b.Property(x => x.Currency).IsRequired().HasMaxLength(LedgerConsts.MaxCurrencyLength);
        b.Property(x => x.Bucket).IsRequired().HasMaxLength(LedgerConsts.MaxBucketLength);
        b.HasIndex(x => new { x.TenantId, x.AccountId, x.OwnerType, x.OwnerId, x.Currency, x.Bucket }).IsUnique().AreNullsDistinct(false);
    }
}
