using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Collection.Data;

public class CollectionRefundConfiguration : IEntityTypeConfiguration<ColCollectionRefund>
{
    internal const string ChannelRefundNoIndex = "UX_ColCollectionRefunds_Channel_Account_RefundNo";

    public void Configure(EntityTypeBuilder<ColCollectionRefund> builder)
    {
        builder.ToTable(CollectionConsts.DbTablePrefix + "CollectionRefunds",
            table => table.HasCheckConstraint("CK_ColCollectionRefunds_PositiveAmount", "\"Amount\" > 0"));
        builder.ConfigureByConvention();
        builder.Property(row => row.Amount).HasPrecision(28, 18);
        builder.Property(row => row.MerchantRefundNo).HasMaxLength(CollectionConsts.MaxCollectionNoLength).IsRequired();
        builder.Property(row => row.MerchantAccountId).HasMaxLength(CollectionConsts.MaxTradeNoLength).IsRequired();
        builder.Property(row => row.ChannelRefundNo).HasMaxLength(CollectionConsts.MaxTradeNoLength);
        builder.Property(row => row.StatusNote).HasMaxLength(CollectionConsts.MaxNoteLength);
        builder.HasOne<ColCollection>().WithMany().HasForeignKey(row => row.CollectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.CollectionId, row.RequestId }).IsUnique();
        builder.HasIndex(row => row.MerchantRefundNo).IsUnique();
        builder.HasIndex(row => new { row.Channel, row.MerchantAccountId, row.ChannelRefundNo })
            .HasDatabaseName(ChannelRefundNoIndex).IsUnique().HasFilter("\"ChannelRefundNo\" IS NOT NULL");
        builder.HasIndex(row => new { row.Status, row.LastCheckedAt, row.Id });
    }
}
