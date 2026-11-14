using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Fakes;
using AbpAdmin.Collection.Http;
using AbpAdmin.Collection.Payments;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Shouldly;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace AbpAdmin.Collection;

public class CollectionChannelNotificationTests : CollectionTestBase
{
    [Fact]
    public async Task Only_Verified_Notification_For_The_Configured_Merchant_Confirms_Payment()
    {
        var collections = GetRequiredService<ICollectionIntegrationService>();
        var receipt = await collections.CreateAsync(new CreateCollectionInput
        {
            UserId = Guid.NewGuid(),
            BusinessId = Guid.NewGuid(),
            BusinessType = "CallbackTest",
            Channel = CollectionChannel.WechatNative,
            Currency = CollectionCurrency.Cny,
            Amount = "10.01",
            ExpiresAt = DateTime.UtcNow.AddMinutes(10),
        });
        var verifier = new TestVerifier();
        var callback = new CollectionChannelNotificationController(
            [verifier], GetRequiredService<CollectionPaymentConfirmationService>(),
            GetRequiredService<ICollectionRefundService>(), GetRequiredService<ICurrentTenant>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        var evidence = new CollectionPaymentObservation(receipt.Channel, verifier.MerchantAccountId,
            receipt.CollectionNo, "verified-trade", 10.01m, receipt.Currency);

        (await callback.NotifyAsync(receipt.Channel, "unconfigured", default)).ShouldBeOfType<NotFoundResult>();
        (await callback.NotifyAsync(receipt.Channel, verifier.MerchantAccountId, default)).ShouldBeOfType<BadRequestResult>();
        verifier.Result = new VerifiedCollectionPayment(evidence with { MerchantAccountId = "other-merchant" });
        (await callback.NotifyAsync(receipt.Channel, verifier.MerchantAccountId, default)).ShouldBeOfType<BadRequestResult>();
        (await collections.GetAsync(receipt.Id))!.Status.ShouldBe(CollectionStatus.Pending);

        verifier.Result = new VerifiedCollectionPayment(evidence);
        (await callback.NotifyAsync(receipt.Channel, verifier.MerchantAccountId, default)).ShouldBeOfType<OkResult>();
        (await callback.NotifyAsync(receipt.Channel, verifier.MerchantAccountId, default)).ShouldBeOfType<OkResult>();
        var completed = await collections.GetAsync(receipt.Id);
        completed!.Status.ShouldBe(CollectionStatus.Completed);
        completed.ChannelTradeNo.ShouldBe(evidence.TradeNo);
    }

    private sealed class TestVerifier : ICollectionChannelNotificationVerifier
    {
        public CollectionChannel Channel => CollectionChannel.WechatNative;
        public string MerchantAccountId => FakeChannelGateway.Wechat.MerchantAccountId;
        public VerifiedCollectionChannelNotification? Result { get; set; }

        public Task<VerifiedCollectionChannelNotification?> VerifyAsync(
            HttpRequest request, CancellationToken cancellationToken) => Task.FromResult(Result);

        public IActionResult Acknowledge(bool accepted) => accepted ? new OkResult() : new BadRequestResult();
    }
}
