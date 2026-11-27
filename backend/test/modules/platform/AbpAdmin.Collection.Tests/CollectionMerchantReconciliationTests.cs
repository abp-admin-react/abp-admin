using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Xunit;

namespace AbpAdmin.Collection;

public class CollectionMerchantReconciliationTests : CollectionTestBase
{
    private const string Slow = "slow-merchant";
    private const string Fast = "fast-merchant";
    private const CollectionChannel Channel = CollectionChannel.WechatNative;

    protected override void BeforeAddApplication(IServiceCollection services)
    {
        base.BeforeAddApplication(services);
        foreach (var merchant in new[] { Slow, Fast })
        {
            var payments = Substitute.For<ICollectionChannelGateway>();
            payments.Channel.Returns(Channel);
            payments.MerchantAccountId.Returns(merchant);
            services.AddSingleton(payments);
            var refunds = Substitute.For<ICollectionRefundChannelGateway>();
            refunds.Channel.Returns(Channel);
            refunds.MerchantAccountId.Returns(merchant);
            services.AddSingleton(refunds);
        }
    }

    [Fact]
    public async Task A_Blocked_Merchant_Does_Not_Block_Later_Payment_Or_Refund_Rounds_For_Another_Merchant()
    {
        var release = Signal();
        var (paymentEntered, refundEntered) = ConfigureGateways(release.Task);
        await SeedAsync(Slow);
        var worker = new CollectionPaymentReconciliationWorker(GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CollectionPaymentReconciliationWorker>.Instance);
        using var stop = new CancellationTokenSource();
        var loops = worker.RunMerchantLoopsAsync(TimeSpan.FromMilliseconds(75), stop.Token);
        try
        {
            await Task.WhenAll(paymentEntered, refundEntered).WaitAsync(TimeSpan.FromSeconds(10));
            var (payment, refund) = await SeedAsync(Fast);
            await WaitForSettlementAsync(payment, refund);
            // A concurrent manual round sees the same merchant lock as the scheduled round.
            await GetRequiredService<ICollectionRefundService>().ReconcileAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var slowRefunds = ServiceProvider.GetServices<ICollectionRefundChannelGateway>().Single(g => g.MerchantAccountId == Slow);
            await slowRefunds.Received(1).QueryRefundAsync(Arg.Any<CollectionRefundChannelRequest>(), Arg.Any<CancellationToken>());
            release.Task.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult();
            await stop.CancelAsync();
            await loops.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task Explicit_Refund_Reconciliation_Runs_Distinct_Merchants_Independently()
    {
        var release = Signal();
        var (_, entered) = ConfigureGateways(release.Task);
        await SeedAsync(Slow);
        var (_, fastRefund) = await SeedAsync(Fast);
        var round = GetRequiredService<ICollectionRefundService>().ReconcileAsync();
        try
        {
            await entered.WaitAsync(TimeSpan.FromSeconds(10));
            await WaitForSettlementAsync(null, fastRefund);
            round.IsCompleted.ShouldBeFalse();
        }
        finally
        {
            release.TrySetResult();
            await round.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private (Task Payment, Task Refund) ConfigureGateways(Task release)
    {
        var paymentEntered = Signal();
        var refundEntered = Signal();
        foreach (var gateway in ServiceProvider.GetServices<ICollectionChannelGateway>().Where(g => g.MerchantAccountId is Slow or Fast))
        {
            gateway.QueryAsync(Arg.Any<ChannelQueryRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var request = call.Arg<ChannelQueryRequest>();
                if (gateway.MerchantAccountId == Slow)
                {
                    paymentEntered.TrySetResult();
                    await release.WaitAsync(call.Arg<CancellationToken>());
                }
                return new ChannelQueryResult(ChannelQueryOutcome.Paid, new CollectionPaymentObservation(Channel,
                    gateway.MerchantAccountId, request.CollectionNo, "trade-" + request.CollectionNo, 10m, CollectionCurrency.Cny));
            });
        }
        foreach (var gateway in ServiceProvider.GetServices<ICollectionRefundChannelGateway>().Where(g => g.MerchantAccountId is Slow or Fast))
        {
            gateway.QueryRefundAsync(Arg.Any<CollectionRefundChannelRequest>(), Arg.Any<CancellationToken>()).Returns(async call =>
            {
                var request = call.Arg<CollectionRefundChannelRequest>();
                if (gateway.MerchantAccountId == Slow)
                {
                    refundEntered.TrySetResult();
                    await release.WaitAsync(call.Arg<CancellationToken>());
                }
                return new CollectionRefundChannelResult(CollectionRefundOutcome.Succeeded, new CollectionRefundConfirmationInput(
                    Channel, gateway.MerchantAccountId, request.MerchantRefundNo, request.CollectionNo, "2", CollectionCurrency.Cny,
                    CollectionRefundOutcome.Succeeded, "refund-" + request.MerchantRefundNo));
            });
        }
        return (paymentEntered.Task, refundEntered.Task);
    }

    private async Task<(Guid Payment, Guid Refund)> SeedAsync(string merchant)
    {
        var pending = CreatePayment(merchant, CollectionStatus.Pending);
        var paid = CreatePayment(merchant, CollectionStatus.Completed);
        var refundId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            db.Collections.AddRange(pending, paid);
            db.Set<ColCollectionRefund>().Add(new ColCollectionRefund(refundId)
            {
                CollectionId = paid.Id, RequestId = Guid.NewGuid(), Channel = Channel, MerchantAccountId = merchant,
                MerchantRefundNo = refundId.ToString("N"), Currency = CollectionCurrency.Cny, Amount = 2m,
                Status = CollectionRefundStatus.Pending, CreationTime = DateTime.UtcNow.AddMinutes(-5),
                LastCheckedAt = DateTime.UtcNow.AddMinutes(-5), NextSubmissionAt = DateTime.UtcNow,
            });
        });
        return (pending.Id, refundId);
    }

    private static ColCollection CreatePayment(string merchant, CollectionStatus status)
    {
        var id = Guid.NewGuid();
        return new ColCollection(id)
        {
            CollectionNo = id.ToString("N"), UserId = Guid.NewGuid(), BusinessId = Guid.NewGuid(), BusinessType = "Order",
            Channel = Channel, MerchantAccountId = merchant, Amount = 10m, Currency = CollectionCurrency.Cny, Status = status,
            ChannelOrderCreated = true, ChannelTradeNo = status == CollectionStatus.Completed ? "trade-" + id.ToString("N") : null,
            CreationTime = DateTime.UtcNow.AddMinutes(-5), ExpiresAt = DateTime.UtcNow.AddDays(1),
        };
    }

    private async Task WaitForSettlementAsync(Guid? payment, Guid refund)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            var settled = await WithUnitOfWorkAsync(async () =>
            {
                var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
                return (!payment.HasValue || await db.Collections.AnyAsync(row => row.Id == payment && row.Status == CollectionStatus.Completed))
                    && await db.Set<ColCollectionRefund>().AnyAsync(row => row.Id == refund && row.Status == CollectionRefundStatus.Succeeded);
            });
            if (settled) return;
            await Task.Delay(50, timeout.Token);
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
