using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;

namespace AbpAdmin.Collection.Channels;

/// <summary>原路退款能力。渠道必须用 MerchantRefundNo 对重复发起请求去重。</summary>
public interface ICollectionRefundChannelGateway
{
    CollectionChannel Channel { get; }
    string MerchantAccountId { get; }

    Task<CollectionRefundChannelResult> RefundAsync(
        CollectionRefundChannelRequest request, CancellationToken cancellationToken = default);

    Task<CollectionRefundChannelResult> QueryRefundAsync(
        CollectionRefundChannelRequest request, CancellationToken cancellationToken = default);
}

public record CollectionRefundChannelRequest(
    string MerchantRefundNo,
    string CollectionNo,
    string ChannelTradeNo,
    decimal PaidAmount,
    decimal RefundAmount,
    CollectionCurrency Currency);

/// <summary>Pending、Succeeded、Failed 均须提供与 Outcome 对应的可信证据。NotFound 仅用于渠道已明确查无此单。</summary>
public record CollectionRefundChannelResult(
    CollectionRefundOutcome Outcome,
    CollectionRefundConfirmationInput? Confirmation = null);
