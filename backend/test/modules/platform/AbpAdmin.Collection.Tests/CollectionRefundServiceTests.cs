using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Fakes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

public class CollectionRefundServiceTests : CollectionTestBase
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(15);
    private static FakeRefundChannelGateway Gateway => FakeRefundChannelGateway.Instance;

    public CollectionRefundServiceTests()
    {
        Gateway.OnSubmit = null;
        Gateway.OnQuery = null;
    }

    [Fact]
    public async Task Pending_Refund_Reserves_Capacity_And_Duplicate_Request_Reuses_The_Original()
    {
        var collection = await CreateCompletedCollectionAsync();
        var service = GetRequiredService<ICollectionRefundService>();
        var submitted = new TaskCompletionSource<CollectionRefundChannelRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        Gateway.OnSubmit = async request =>
        {
            Interlocked.Increment(ref calls);
            var activeTransaction = GetRequiredService<IUnitOfWorkManager>().Current?.Options.IsTransactional;
            if (activeTransaction == true)
            {
                submitted.TrySetException(new InvalidOperationException("Refund channel ran inside a database transaction."));
            }
            submitted.TrySetResult(request);
            await release.Task.WaitAsync(TestTimeout);
            return new(CollectionRefundOutcome.Unknown);
        };

        var input = Input(collection.Id, "70");
        var first = InScopeAsync(refunds => refunds.RequestAsync(input));
        try
        {
            var request = await submitted.Task.WaitAsync(TestTimeout);
            request.PaidAmount.ShouldBe(100m);
            request.RefundAmount.ShouldBe(70m);
            request.Currency.ShouldBe(CollectionCurrency.Cny);
            var duplicate = await service.RequestAsync(input);
            duplicate.MerchantRefundNo.ShouldBe(request.MerchantRefundNo);
            duplicate.Status.ShouldBe(CollectionRefundStatus.Pending);
            calls.ShouldBe(1);

            var tooMuch = await Should.ThrowAsync<BusinessException>(() => service.RequestAsync(Input(collection.Id, "40")));
            tooMuch.Code.ShouldBe(CollectionRefundErrorCodes.RefundAmountExceeded);
            var wrongAmount = Input(collection.Id, "69");
            wrongAmount.RequestId = input.RequestId;
            var conflict = await Should.ThrowAsync<BusinessException>(() => service.RequestAsync(wrongAmount));
            conflict.Code.ShouldBe(CollectionRefundErrorCodes.RefundIdempotencyConflict);
        }
        finally
        {
            release.TrySetResult();
        }

        var initial = await first;
        initial.ChannelResultUnknown.ShouldBeTrue();
        (await service.ListAsync(collection.Id)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_Provider_Notifications_Record_One_Success_And_One_Event()
    {
        var collection = await CreateCompletedCollectionAsync();
        var service = GetRequiredService<ICollectionRefundService>();
        var refund = await service.RequestAsync(Input(collection.Id, "70"));
        var evidence = Evidence(refund, collection, CollectionRefundOutcome.Succeeded, "same-provider-refund");
        var wrongMerchant = await Should.ThrowAsync<BusinessException>(() =>
            service.ConfirmAsync(evidence with { MerchantAccountId = "different-merchant" }));
        wrongMerchant.Code.ShouldBe(CollectionRefundErrorCodes.RefundEvidenceMismatch);
        var wrongAmount = await Should.ThrowAsync<BusinessException>(() =>
            service.ConfirmAsync(evidence with { Amount = "69.99" }));
        wrongAmount.Code.ShouldBe(CollectionRefundErrorCodes.RefundEvidenceMismatch);

        var results = await Task.WhenAll(
            InScopeAsync(refunds => refunds.ConfirmAsync(evidence)),
            InScopeAsync(refunds => refunds.ConfirmAsync(evidence)));
        results.All(result => result.Status == CollectionRefundStatus.Succeeded).ShouldBeTrue();
        results[0].SucceededAt.ShouldBe(results[1].SucceededAt);
        (await CountRefundEventsAsync(refund.Id)).ShouldBe(1);

        var tooMuch = await Should.ThrowAsync<BusinessException>(() => service.RequestAsync(Input(collection.Id, "30.01")));
        tooMuch.Code.ShouldBe(CollectionRefundErrorCodes.RefundAmountExceeded);

        var stale = await service.ConfirmAsync(evidence with { Outcome = CollectionRefundOutcome.Pending });
        stale.Status.ShouldBe(CollectionRefundStatus.Succeeded);
        (await CountRefundEventsAsync(refund.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Unknown_Refund_Retries_Original_Identity_Once_When_Query_Confirms_Not_Found()
    {
        var collection = await CreateCompletedCollectionAsync();
        var service = GetRequiredService<ICollectionRefundService>();
        string? firstRefundNo = null;
        var calls = 0;
        Gateway.OnSubmit = request =>
        {
            Interlocked.Increment(ref calls);
            firstRefundNo = request.MerchantRefundNo;
            throw new TimeoutException();
        };
        var refund = await service.RequestAsync(Input(collection.Id, "15.05"));
        refund.ChannelResultUnknown.ShouldBeTrue();
        firstRefundNo.ShouldBe(refund.MerchantRefundNo);

        // 模拟进程退出后提交租约过期，补偿按同一商户退款号恢复。
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.Set<ColCollectionRefund>().Where(row => row.Id == refund.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.NextSubmissionAt, DateTime.UtcNow.AddMinutes(-2)));
        });
        var queried = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queryCalls = 0;
        Gateway.OnQuery = async request =>
        {
            request.MerchantRefundNo.ShouldBe(firstRefundNo);
            if (Interlocked.Increment(ref queryCalls) == 2)
            {
                queried.TrySetResult();
            }
            await queried.Task.WaitAsync(TestTimeout);
            return new(CollectionRefundOutcome.NotFound);
        };
        Gateway.OnSubmit = request =>
        {
            Interlocked.Increment(ref calls);
            request.MerchantRefundNo.ShouldBe(firstRefundNo);
            return Task.FromResult(new CollectionRefundChannelResult(CollectionRefundOutcome.Succeeded,
                Evidence(refund, collection, CollectionRefundOutcome.Succeeded, "recovered-provider-refund")));
        };

        await Task.WhenAll(
            InScopeAsync(refunds => refunds.QueryAsync(refund.Id)),
            InScopeAsync(refunds => refunds.QueryAsync(refund.Id)));
        (await service.GetAsync(refund.Id)).Status.ShouldBe(CollectionRefundStatus.Succeeded);
        calls.ShouldBe(2); // 首次超时 + 一次原单重试
        (await CountRefundEventsAsync(refund.Id)).ShouldBe(1);
    }

    [Fact]
    public async Task Definitive_Failure_Releases_Capacity_And_Provider_Refund_Identity_Is_Unique()
    {
        var collection = await CreateCompletedCollectionAsync();
        var service = GetRequiredService<ICollectionRefundService>();
        var failed = await service.RequestAsync(Input(collection.Id, "100"));
        var failure = await service.ConfirmAsync(Evidence(failed, collection, CollectionRefundOutcome.Failed));
        failure.Status.ShouldBe(CollectionRefundStatus.Failed);
        (await CountRefundEventsAsync(failed.Id)).ShouldBe(0);

        var wrongCurrency = Input(collection.Id, "100");
        wrongCurrency.Currency = CollectionCurrency.Usd;
        var currencyError = await Should.ThrowAsync<BusinessException>(() => service.RequestAsync(wrongCurrency));
        currencyError.Code.ShouldBe(CollectionRefundErrorCodes.RefundNotAllowed);

        var replacement = await service.RequestAsync(Input(collection.Id, "100"));
        await service.ConfirmAsync(Evidence(replacement, collection, CollectionRefundOutcome.Succeeded, "provider-refund"));

        var other = await CreateCompletedCollectionAsync();
        var otherRefund = await service.RequestAsync(Input(other.Id, "100"));
        var conflict = await Should.ThrowAsync<BusinessException>(() => service.ConfirmAsync(
            Evidence(otherRefund, other, CollectionRefundOutcome.Succeeded, "provider-refund")));
        conflict.Code.ShouldBe(CollectionRefundErrorCodes.RefundProviderReferenceConflict);
        (await service.GetAsync(otherRefund.Id)).Status.ShouldBe(CollectionRefundStatus.Pending);
        (await CountRefundEventsAsync(otherRefund.Id)).ShouldBe(0);
    }

    [Fact]
    public async Task Inconsistent_Provider_Evidence_Keeps_The_Refund_Reserved_Until_Verified()
    {
        var collection = await CreateCompletedCollectionAsync();
        var service = GetRequiredService<ICollectionRefundService>();
        var input = Input(collection.Id, "100");
        Gateway.OnSubmit = request => Task.FromResult(new CollectionRefundChannelResult(CollectionRefundOutcome.Succeeded,
            new(CollectionChannel.WechatNative, FakeRefundChannelGateway.AccountId, request.MerchantRefundNo,
                collection.CollectionNo, "99", input.Currency, CollectionRefundOutcome.Succeeded, "provider-refund")));
        var mismatch = await Should.ThrowAsync<BusinessException>(() => service.RequestAsync(input));
        mismatch.Code.ShouldBe(CollectionRefundErrorCodes.RefundEvidenceMismatch);

        var refund = (await service.ListAsync(collection.Id)).Single();
        refund.Status.ShouldBe(CollectionRefundStatus.Pending);
        refund.ChannelResultUnknown.ShouldBeTrue();
        (await CountRefundEventsAsync(refund.Id)).ShouldBe(0);

        var provisional = Evidence(refund, collection, CollectionRefundOutcome.Pending, "provider-refund");
        await service.ConfirmAsync(provisional);
        var unboundFailure = await Should.ThrowAsync<BusinessException>(() => service.ConfirmAsync(
            provisional with { Outcome = CollectionRefundOutcome.Failed, ChannelRefundNo = null }));
        unboundFailure.Code.ShouldBe(CollectionRefundErrorCodes.RefundEvidenceMismatch);
        (await service.GetAsync(refund.Id)).Status.ShouldBe(CollectionRefundStatus.Pending);

        Gateway.OnQuery = _ => Task.FromResult(new CollectionRefundChannelResult(CollectionRefundOutcome.Failed,
            provisional with { Outcome = CollectionRefundOutcome.Failed }));
        (await service.QueryAsync(refund.Id)).Status.ShouldBe(CollectionRefundStatus.Failed);
    }

    private async Task<T> InScopeAsync<T>(Func<ICollectionRefundService, Task<T>> action)
    {
        using var scope = ServiceProvider.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ICollectionRefundService>());
    }

    private async Task<int> CountRefundEventsAsync(Guid refundId)
    {
        using var scope = ServiceProvider.CreateScope();
        using var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false);
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var count = await db.Database.SqlQuery<int>($"""
            SELECT count(*)::int AS "Value" FROM "ColPaymentEvents" WHERE "Id" = {refundId}
            """).SingleAsync();
        await uow.CompleteAsync();
        return count;
    }

    private async Task<ColCollection> CreateCompletedCollectionAsync()
    {
        var id = Guid.NewGuid();
        var collection = new ColCollection(id)
        {
            CollectionNo = $"REFUND-TEST-{id:N}",
            UserId = Guid.NewGuid(),
            BusinessType = "Order",
            BusinessId = Guid.NewGuid(),
            Channel = CollectionChannel.WechatNative,
            Currency = CollectionCurrency.Cny,
            Amount = 100m,
            Status = CollectionStatus.Completed,
            MerchantAccountId = FakeRefundChannelGateway.AccountId,
            ChannelTradeNo = $"TRADE-{id:N}",
            CompletedAt = DateTime.UtcNow,
            CreationTime = DateTime.UtcNow,
        };
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            db.Collections.Add(collection);
            await db.SaveChangesAsync();
        });
        return collection;
    }

    private static RequestCollectionRefundInput Input(Guid collectionId, string amount) => new()
    {
        CollectionId = collectionId,
        RequestId = Guid.NewGuid(),
        Amount = amount,
        Currency = CollectionCurrency.Cny,
    };

    private static CollectionRefundConfirmationInput Evidence(
        CollectionRefundDto refund, ColCollection collection, CollectionRefundOutcome outcome, string? providerNo = null) =>
        new(refund.Channel, FakeRefundChannelGateway.AccountId, refund.MerchantRefundNo, collection.CollectionNo,
            refund.Amount, refund.Currency, outcome, providerNo);
}
