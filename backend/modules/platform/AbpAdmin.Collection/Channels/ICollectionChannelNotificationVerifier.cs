using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AbpAdmin.Collection.Channels;

/// <summary>Provider boundary. Verify the original request before returning normalized evidence.</summary>
public interface ICollectionChannelNotificationVerifier
{
    CollectionChannel Channel { get; }
    string MerchantAccountId { get; }

    Task<VerifiedCollectionChannelNotification?> VerifyAsync(
        HttpRequest request, CancellationToken cancellationToken);

    IActionResult Acknowledge(bool accepted);
}

public abstract record VerifiedCollectionChannelNotification;

public sealed record VerifiedCollectionPayment(CollectionPaymentObservation Observation)
    : VerifiedCollectionChannelNotification;

public sealed record VerifiedCollectionRefund(CollectionRefundConfirmationInput Observation)
    : VerifiedCollectionChannelNotification;
