using System;
using System.Text.Json;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Collection.Payments;

/// <summary>在收款或退款状态提交的同一事务中记录待投递事件。</summary>
public static class CollectionPaymentEventOutbox
{
    public static Guid Enqueue(CollectionDbContext db, CollectionCompletedEto message) =>
        Enqueue(db, message.CollectionId, CollectionPaymentEventKind.Completed, message, message.CompletedAt);

    public static Guid Enqueue(CollectionDbContext db, CollectionRefundSucceededEto message) =>
        Enqueue(db, message.RefundId, CollectionPaymentEventKind.RefundSucceeded, message, message.SucceededAt);

    private static Guid Enqueue<T>(
        CollectionDbContext db, Guid id, CollectionPaymentEventKind kind, T message, DateTime now)
    {
        db.Set<ColPaymentEvent>().Add(new ColPaymentEvent(id, kind, JsonSerializer.Serialize(message), now));
        return id;
    }
}

internal enum CollectionPaymentEventKind
{
    Completed = 1,
    RefundSucceeded = 2,
}

internal sealed class ColPaymentEvent : Entity<Guid>
{
    private ColPaymentEvent()
    {
    }

    public ColPaymentEvent(Guid id, CollectionPaymentEventKind kind, string payload, DateTime now) : base(id)
    {
        Kind = kind;
        Payload = payload;
        CreatedAt = now;
        NextAttemptAt = now;
    }

    public CollectionPaymentEventKind Kind { get; private set; }
    public string Payload { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime NextAttemptAt { get; private set; }
    public DateTime? DeliveredAt { get; private set; }
    public int FailedAttempts { get; private set; }

    public void MarkDelivered(DateTime now) => DeliveredAt = now;

    public void Defer(DateTime now)
    {
        FailedAttempts++;
        NextAttemptAt = now.AddSeconds(5 * Math.Pow(2, Math.Min(FailedAttempts - 1, 6)));
    }
}

internal sealed class CollectionPaymentEventConfiguration : IEntityTypeConfiguration<ColPaymentEvent>
{
    public void Configure(EntityTypeBuilder<ColPaymentEvent> builder)
    {
        builder.ToTable("ColPaymentEvents");
        builder.ConfigureByConvention();
        builder.Property(item => item.Payload).IsRequired();
        builder.HasIndex(item => new { item.NextAttemptAt, item.CreatedAt })
            .HasFilter("\"DeliveredAt\" IS NULL");
    }
}
