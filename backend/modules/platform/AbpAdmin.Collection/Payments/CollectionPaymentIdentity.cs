using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbpAdmin.Collection.Payments;

public static class CollectionPaymentIdentity
{
    internal const int MaxMerchantAccountIdLength = 128;

    public static void ConfigureCollectionPaymentIdentity(this ModelBuilder builder)
    {
        var collection = builder.Entity<ColCollection>();
        collection.Property(row => row.MerchantAccountId).HasMaxLength(MaxMerchantAccountIdLength);
        collection.HasIndex(row => new { row.Channel, row.MerchantAccountId, row.ChannelTradeNo })
            .IsUnique()
            .HasFilter("""
                "Status" = 1 AND "MerchantAccountId" IS NOT NULL AND "ChannelTradeNo" IS NOT NULL
                """);
        collection.HasIndex(row => new { row.NextChannelQueryAt, row.CreationTime })
            .HasFilter("""
                "Status" = 0 AND "MerchantAccountId" IS NOT NULL
                """);
    }

    internal static Task LockBusinessAsync(
        CollectionDbContext db, string businessType, Guid businessId, CancellationToken cancellationToken = default)
    {
        var key = $"{businessType}:{businessId:N}";
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('Collection:Business'), hashtext({key}))", cancellationToken);
    }

    internal static async Task<ColCollection?> FindForUpdateAsync(
        CollectionDbContext db, Guid id, CancellationToken cancellationToken = default)
    {
        var rows = await db.Collections
            .FromSqlInterpolated($"""SELECT * FROM "ColCollections" WHERE "Id" = {id} FOR UPDATE""")
            .ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }
}
