using System;
using AbpAdmin.Collection.Contracts;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Collection.Entities;

public class ColCollectionRefund : BasicAggregateRoot<Guid>
{
    public ColCollectionRefund(Guid id) : base(id)
    {
    }

    protected ColCollectionRefund()
    {
    }

    public Guid CollectionId { get; set; }
    public Guid RequestId { get; set; }
    public string MerchantRefundNo { get; set; } = default!;
    public string MerchantAccountId { get; set; } = default!;
    public string? ChannelRefundNo { get; set; }
    public CollectionChannel Channel { get; set; }
    public CollectionCurrency Currency { get; set; }
    public decimal Amount { get; set; }
    public CollectionRefundStatus Status { get; set; }
    public bool ChannelResultUnknown { get; set; }
    public string? StatusNote { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? SucceededAt { get; set; }
    public DateTime LastCheckedAt { get; set; }
    public DateTime NextSubmissionAt { get; set; }
}
