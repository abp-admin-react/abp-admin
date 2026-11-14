using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Http;

[Route("api/collection/channel-notifications")]
[SkipStatusCodePages]
public class CollectionChannelNotificationController(
    IEnumerable<ICollectionChannelNotificationVerifier> verifiers,
    CollectionPaymentConfirmationService payments,
    ICollectionRefundService refunds,
    ICurrentTenant currentTenant) : ControllerBase
{
    [HttpPost("{channel}/{merchantAccountId}")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(65536)]
    [UnitOfWork(IsDisabled = true)]
    public async Task<IActionResult> NotifyAsync(
        CollectionChannel channel, string merchantAccountId, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(channel) || merchantAccountId.Length > CollectionConsts.MaxTradeNoLength)
        {
            return NotFound();
        }

        var verifier = verifiers.SingleOrDefault(item =>
            item.Channel == channel && item.MerchantAccountId == merchantAccountId);
        if (verifier == null)
        {
            return NotFound();
        }

        using var host = currentTenant.Change(null);
        var verified = await verifier.VerifyAsync(Request, cancellationToken);
        var accepted = verified switch
        {
            VerifiedCollectionPayment(var payment)
                when payment.Channel == channel && payment.MerchantAccountId == merchantAccountId
                => await ApplyPaymentAsync(payment, cancellationToken),
            VerifiedCollectionRefund(var refund)
                when refund.Channel == channel && refund.MerchantAccountId == merchantAccountId
                => await ApplyRefundAsync(refund, cancellationToken),
            _ => false,
        };
        return verifier.Acknowledge(accepted);
    }

    private async Task<bool> ApplyPaymentAsync(
        CollectionPaymentObservation observation, CancellationToken cancellationToken)
    {
        var result = await payments.ConfirmAsync(observation, cancellationToken);
        return result.Outcome is CollectionPaymentConfirmationOutcome.Accepted
            or CollectionPaymentConfirmationOutcome.Duplicate;
    }

    private async Task<bool> ApplyRefundAsync(
        CollectionRefundConfirmationInput observation, CancellationToken cancellationToken)
    {
        if (observation.Outcome is not (CollectionRefundOutcome.Pending or CollectionRefundOutcome.Succeeded
            or CollectionRefundOutcome.Failed))
        {
            return false;
        }

        try
        {
            await refunds.ConfirmAsync(observation, cancellationToken);
            return true;
        }
        catch (EntityNotFoundException)
        {
            return false;
        }
        catch (BusinessException exception) when (exception.Code is CollectionRefundErrorCodes.RefundEvidenceMismatch
            or CollectionRefundErrorCodes.RefundProviderReferenceConflict)
        {
            return false;
        }
    }
}
