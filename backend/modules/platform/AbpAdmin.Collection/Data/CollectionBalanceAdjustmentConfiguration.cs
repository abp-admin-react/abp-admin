using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Collection.Data;

public class CollectionBalanceAdjustmentConfiguration : IEntityTypeConfiguration<ColBalanceAdjustment>
{
    public void Configure(EntityTypeBuilder<ColBalanceAdjustment> builder)
    {
        builder.ToTable("ColBalanceAdjustments");
        builder.ConfigureByConvention();
        builder.Property(row => row.Amount).HasPrecision(28, 18);
        builder.Property(row => row.BalanceAfter).HasPrecision(28, 18);
        builder.Property(row => row.Reason).HasMaxLength(512).IsRequired();
        builder.Property(row => row.ReviewNote).HasMaxLength(512);
        builder.HasIndex(row => row.RequestId).IsUnique();
        builder.HasIndex(row => new { row.Status, row.CreationTime });
        builder.HasIndex(row => new { row.UserId, row.CreationTime });
        builder.HasIndex(row => row.BalanceTxnId).IsUnique();
        builder.HasIndex(row => row.LedgerVoucherId).IsUnique();
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_ColBalanceAdjustments_Amount", "\"Amount\" <> 0");
            table.HasCheckConstraint("CK_ColBalanceAdjustments_Currency", "\"Currency\" IN (3, 4)");
        });
    }
}
