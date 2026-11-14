using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Payments;

public class CollectionPaymentEventDispatcher(
    IDbContextProvider<CollectionDbContext> dbContextProvider,
    IUnitOfWorkManager unitOfWorkManager,
    IDistributedEventBus eventBus,
    IClock clock,
    ILogger<CollectionPaymentEventDispatcher> logger) : ITransientDependency
{
    public async Task DispatchPendingAsync(CancellationToken cancellationToken = default)
    {
        Guid[] ids;
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var db = await dbContextProvider.GetDbContextAsync();
            var now = clock.Now;
            ids = await db.Set<ColPaymentEvent>()
                .Where(item => item.DeliveredAt == null && item.NextAttemptAt <= now)
                .OrderBy(item => item.NextAttemptAt)
                .ThenBy(item => item.CreatedAt)
                .ThenBy(item => item.Id)
                .Select(item => item.Id)
                .Take(50)
                .ToArrayAsync(cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }

        foreach (var id in ids)
        {
            await DispatchAsync(id, cancellationToken);
        }
    }

    public async Task DispatchAsync(Guid eventId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            var item = await LockEventAsync(eventId, cancellationToken);
            if (item != null)
            {
                // 发布与投递标记的提交之间可能中断，消费者必须能处理重复事件。
                await PublishAsync(item);
                item.MarkDelivered(clock.Now);
            }

            await uow.CompleteAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "收款事件 {EventId} 投递或记录投递结果失败，未完成的事件会重试", eventId);
            using var retryUow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
            (await LockEventAsync(eventId, cancellationToken))?.Defer(clock.Now);
            await retryUow.CompleteAsync(cancellationToken);
        }
    }

    private async Task<ColPaymentEvent?> LockEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var db = await dbContextProvider.GetDbContextAsync();
        var now = clock.Now;
        var rows = await db.Set<ColPaymentEvent>().FromSqlInterpolated($"""
            SELECT * FROM "ColPaymentEvents"
            WHERE "Id" = {eventId} AND "DeliveredAt" IS NULL AND "NextAttemptAt" <= {now}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);
        return rows.SingleOrDefault();
    }

    private Task PublishAsync(ColPaymentEvent item) => item.Kind switch
    {
        CollectionPaymentEventKind.Completed => eventBus.PublishAsync(
            Read<CollectionCompletedEto>(item), onUnitOfWorkComplete: false, useOutbox: false),
        CollectionPaymentEventKind.RefundSucceeded => eventBus.PublishAsync(
            Read<CollectionRefundSucceededEto>(item), onUnitOfWorkComplete: false, useOutbox: false),
        _ => throw new InvalidOperationException($"Unsupported payment event kind {item.Kind}."),
    };

    private static T Read<T>(ColPaymentEvent item) =>
        JsonSerializer.Deserialize<T>(item.Payload) ?? throw new JsonException("Missing payment event payload.");
}
