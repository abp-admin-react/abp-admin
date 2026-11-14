using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;

namespace AbpAdmin.Collection.Contracts;

public enum CollectionRefundStatus
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
}

/// <summary>渠道 Failed 仅表示已终结且明确未退款。超时、临时拒绝和仍有可能成功的状态保留为 Pending 或 Unknown。</summary>
public enum CollectionRefundOutcome
{
    Pending = 0,
    Succeeded = 1,
    Failed = 2,
    NotFound = 3,
    Unknown = 4,
}

public class RequestCollectionRefundInput
{
    public Guid CollectionId { get; set; }

    /// <summary>调用方为一次退款生成的幂等键。重试时沿用原键。</summary>
    public Guid RequestId { get; set; }

    [Required, StringLength(64)]
    public string Amount { get; set; } = default!;

    [EnumDataType(typeof(CollectionCurrency))]
    public CollectionCurrency Currency { get; set; }
}

/// <summary>渠道验签或可信查单后归一化的退款事实。</summary>
public record CollectionRefundConfirmationInput(
    CollectionChannel Channel,
    string MerchantAccountId,
    string MerchantRefundNo,
    string CollectionNo,
    string Amount,
    CollectionCurrency Currency,
    CollectionRefundOutcome Outcome,
    string? ChannelRefundNo = null,
    string? StatusNote = null);

public class CollectionRefundDto
{
    public Guid Id { get; init; }
    public Guid CollectionId { get; init; }
    public Guid RequestId { get; init; }
    public string MerchantRefundNo { get; init; } = default!;
    public string? ChannelRefundNo { get; init; }
    public CollectionChannel Channel { get; init; }
    public string Amount { get; init; } = default!;
    public CollectionCurrency Currency { get; init; }
    public CollectionRefundStatus Status { get; init; }
    public bool ChannelResultUnknown { get; init; }
    public string? StatusNote { get; init; }
    public DateTime CreationTime { get; init; }
    public DateTime? SucceededAt { get; init; }
}

public interface ICollectionRefundService
{
    Task<CollectionRefundDto> RequestAsync(RequestCollectionRefundInput input, CancellationToken cancellationToken = default);
    Task<CollectionRefundDto> GetAsync(Guid refundId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CollectionRefundDto>> ListAsync(Guid collectionId, CancellationToken cancellationToken = default);
    Task<CollectionRefundDto> QueryAsync(Guid refundId, CancellationToken cancellationToken = default);
    Task<CollectionRefundDto> ConfirmAsync(CollectionRefundConfirmationInput input, CancellationToken cancellationToken = default);
    Task ReconcileAsync(CancellationToken cancellationToken = default);
}

[Serializable]
public class CollectionRefundSucceededEto
{
    public Guid RefundId { get; set; }
    public Guid CollectionId { get; set; }
    public string BusinessType { get; set; } = default!;
    public Guid BusinessId { get; set; }
    public CollectionChannel Channel { get; set; }
    public string Amount { get; set; } = default!;
    public CollectionCurrency Currency { get; set; }
    public string ChannelRefundNo { get; set; } = default!;
    public DateTime SucceededAt { get; set; }
}

public static class CollectionRefundErrorCodes
{
    public const string RefundNotAllowed = CollectionErrorCodes.Namespace + ":RefundNotAllowed";
    public const string RefundAmountExceeded = CollectionErrorCodes.Namespace + ":RefundAmountExceeded";
    public const string RefundIdempotencyConflict = CollectionErrorCodes.Namespace + ":RefundIdempotencyConflict";
    public const string RefundEvidenceMismatch = CollectionErrorCodes.Namespace + ":RefundEvidenceMismatch";
    public const string RefundProviderReferenceConflict = CollectionErrorCodes.Namespace + ":RefundProviderReferenceConflict";
}
