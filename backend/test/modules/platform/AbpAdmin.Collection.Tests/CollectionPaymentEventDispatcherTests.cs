using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EventBus.Distributed;
using Xunit;

namespace AbpAdmin.Collection;

public class CollectionPaymentEventDispatcherTests : CollectionTestBase
{
    [Fact]
    public async Task Failed_Delivery_Remains_Queued_And_Concurrent_Retry_Delivers_Once()
    {
        var id = Guid.NewGuid();
        var message = new CollectionCompletedEto
        {
            CollectionId = id,
            BusinessType = PaymentEventTestHandler.BusinessType,
            CompletedAt = DateTime.UtcNow.AddSeconds(-1),
        };
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            CollectionPaymentEventOutbox.Enqueue(db, message);
        });

        PaymentEventTestHandler.Reset();
        await DispatchAsync(id);
        PaymentEventTestHandler.Attempts.ShouldBe(1);
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            (await db.Database.SqlQuery<int>($"""
                SELECT "FailedAttempts" AS "Value" FROM "ColPaymentEvents" WHERE "Id" = {id} AND "DeliveredAt" IS NULL
                """).SingleAsync()).ShouldBe(1);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "ColPaymentEvents" SET "NextAttemptAt" = {DateTime.UtcNow.AddSeconds(-1)} WHERE "Id" = {id}
                """);
        });

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => DispatchAsync(id)));
        PaymentEventTestHandler.Attempts.ShouldBe(2);
        await DispatchAsync(id);
        PaymentEventTestHandler.Attempts.ShouldBe(2);
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            (await db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM "ColPaymentEvents" WHERE "Id" = {id} AND "DeliveredAt" IS NOT NULL
                """).SingleAsync()).ShouldBe(1);
        });
    }

    private async Task DispatchAsync(Guid id)
    {
        using var scope = ServiceProvider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<CollectionPaymentEventDispatcher>().DispatchAsync(id);
    }
}

public sealed class PaymentEventTestHandler : IDistributedEventHandler<CollectionCompletedEto>, ITransientDependency
{
    public const string BusinessType = "CollectionPaymentEventTest";
    private static int _attempts;
    public static int Attempts => Volatile.Read(ref _attempts);

    public static void Reset() => Interlocked.Exchange(ref _attempts, 0);

    public Task HandleEventAsync(CollectionCompletedEto eventData)
    {
        if (eventData.BusinessType == BusinessType && Interlocked.Increment(ref _attempts) == 1)
        {
            throw new InvalidOperationException("Simulated failure after a persisted confirmation.");
        }

        return Task.CompletedTask;
    }
}
