using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Fakes;
using AbpAdmin.Collection.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 收款集成服务本地状态机用例（计划第 3.1、5.1、8 节，Testcontainers PG、程序集串行）：
/// CreateAsync 返回后另一个 scope 能读到 Pending 行；渠道明确失败后索引释放、同渠道重试
/// 能拿到新二维码；超时/未知保持 Pending 并标记；同渠道同金额幂等返回原单、不同渠道冲突；
/// CancelAsync 对非 Pending 不打渠道、渠道订单不存在视为关单成功、关单遇已支付走升级路径。
/// </summary>
public class CollectionIntegrationServiceTests : CollectionTestBase
{
    private static readonly Guid UserId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private FakeChannelGateway Fake => Fakes.FakeChannelGateway.Wechat;

    private CollectionIntegrationService Service =>
        (CollectionIntegrationService)GetRequiredService<AbpAdmin.Collection.Contracts.ICollectionIntegrationService>();

    [Fact]
    public async Task CreateAsync_Pending_Row_Is_Visible_From_Another_Scope()
    {
        var input = Input(amount: "72.45");

        // 调用方持有开了但未提交的环境 UoW（模拟应用服务外层）：内部 requiresNew 提交必须
        // 不受环境 UoW 影响——这是「渠道调用在 requiresNew 提交之后」的强形式
        using var ambient = ServiceProvider.GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>().Begin();
        var created = await Service.CreateAsync(input);

        created.Status.ShouldBe(CollectionStatus.Pending);
        created.ChannelPayload.ShouldNotBeNullOrEmpty();

        var row = await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.Collections.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        });
        row.Status.ShouldBe(CollectionStatus.Pending);
        row.ChannelTradeNo.ShouldNotBeNullOrEmpty(); // 回写已在渠道调用后提交
        await ambient.CompleteAsync();
    }

    [Fact]
    public async Task Explicit_Channel_Failure_Releases_Index_And_Retry_Succeeds()
    {
        var input = Input(amount: "72.45");

        // 渠道明确拒绝（业务失败，非超时）：行改为 ChannelFailed，抛出 ChannelFailed
        Fake.NextCreateResult = new ChannelCreateResult(Succeeded: false, Unknown: false, FailureNote: "rejected");
        var exception = await Should.ThrowAsync<BusinessException>(() => Service.CreateAsync(input));
        exception.Code.ShouldBe(CollectionErrorCodes.ChannelFailed);

        var failedRow = await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.Collections.AsNoTracking().SingleAsync(c => c.BusinessId == input.BusinessId);
        });
        failedRow.Status.ShouldBe(CollectionStatus.ChannelFailed); // 退出 Pending 唯一索引

        // 同渠道同金额重试：索引已释放，新建成功拿到二维码（不是返回空白单）
        var retry = await Service.CreateAsync(input);
        retry.Status.ShouldBe(CollectionStatus.Pending);
        retry.ChannelPayload.ShouldNotBeNullOrEmpty();
        retry.Id.ShouldNotBe(failedRow.Id);
    }

    [Fact]
    public async Task Unknown_Channel_Outcome_Keeps_Pending_With_Flag()
    {
        var input = Input(amount: "72.45");

        // 超时/结果未知：保持 Pending 并标未知，不抛 ChannelFailed
        Fake.NextCreateResult = new ChannelCreateResult(Succeeded: false, Unknown: true);
        var created = await Service.CreateAsync(input);
        created.Status.ShouldBe(CollectionStatus.Pending);
        created.ChannelResultUnknown.ShouldBeTrue();

        var row = await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.Collections.AsNoTracking().SingleAsync(c => c.Id == created.Id);
        });
        row.ChannelResultUnknown.ShouldBeTrue();
        row.Status.ShouldBe(CollectionStatus.Pending);
    }

    [Fact]
    public async Task Same_Channel_And_Amount_Returns_Original_Collection()
    {
        var input = Input(amount: "72.45");

        var first = await Service.CreateAsync(input);
        var second = await Service.CreateAsync(input);

        second.Id.ShouldBe(first.Id); // 幂等：渠道、金额、币种都相同则返回原单
        Fake.CreateCallCount.ShouldBe(1); // 幂等重入不再打渠道：两次服务调用只发生一次网关调用
    }

    [Fact]
    public async Task Different_Channel_Or_Amount_Conflicts()
    {
        var businessId = Guid.NewGuid();
        var created = await Service.CreateAsync(Input(amount: "72.45", businessId: businessId));

        // 渠道不同则冲突（调用方先取消）
        await Should.ThrowAsync<BusinessException>(
            () => Service.CreateAsync(Input(amount: "72.45", channel: CollectionChannel.AlipayPrecreate, businessId: businessId)));

        // 金额不同则冲突
        await Should.ThrowAsync<BusinessException>(
            () => Service.CreateAsync(Input(amount: "80.00", businessId: businessId)));

        // 取消后可以换渠道（Pending 唯一索引随 Closed 释放）
        await Service.CancelAsync(created.Id);
        var alipay = await Service.CreateAsync(Input(amount: "72.45", channel: CollectionChannel.AlipayPrecreate, businessId: businessId));
        alipay.Id.ShouldNotBe(created.Id);
    }

    [Fact]
    public async Task Stripe_With_Less_Than_30_Minutes_Is_Rejected_Without_Row()
    {
        var input = Input(amount: "10.00", channel: CollectionChannel.StripeCheckout,
            expiresAt: DateTime.UtcNow.AddMinutes(20));

        await Should.ThrowAsync<BusinessException>(() => Service.CreateAsync(input));

        (await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.Collections.AsNoTracking().AnyAsync(c => c.BusinessId == input.BusinessId);
        })).ShouldBeFalse(); // 不插行
    }

    [Fact]
    public async Task CancelAsync_Closes_Pending_And_Treats_OrderNotFound_As_Success()
    {
        var created = await Service.CreateAsync(Input(amount: "72.45"));
        Fake.CloseResults[created.CollectionNo] = new ChannelCloseResult(ChannelCloseOutcome.OrderNotFound);

        var result = await Service.CancelAsync(created.Id);

        result.Collection.Status.ShouldBe(CollectionStatus.Closed); // 「订单不存在」视为关单成功
        Fake.CloseCallCount.ShouldBe(1);
    }

    [Fact]
    public async Task CancelAsync_Does_Not_Call_Channel_When_Not_Pending()
    {
        var created = await Service.CreateAsync(Input(amount: "72.45"));
        await Service.CancelAsync(created.Id);
        var closes = Fake.CloseCallCount;

        // 已关闭的单再取消：不请求渠道关单，返回当前状态，不抛异常
        var again = await Service.CancelAsync(created.Id);
        again.Collection.Status.ShouldBe(CollectionStatus.Closed);
        Fake.CloseCallCount.ShouldBe(closes);
    }

    [Fact]
    public async Task CancelAsync_Escalates_When_Channel_Already_Paid()
    {
        var created = await Service.CreateAsync(Input(amount: "72.45"));
        Fake.CloseResults[created.CollectionNo] = new ChannelCloseResult(
            ChannelCloseOutcome.AlreadyPaid, Payment(created));

        var result = await Service.CancelAsync(created.Id);

        // 升级路径：渠道返回已支付、金额核对一致 → 标 Completed（事件在提交后发布）
        result.Collection.Status.ShouldBe(CollectionStatus.Completed);
        result.Collection.CompletedAt.ShouldNotBeNull();
        result.Collection.ChannelTradeNo.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Callback_Query_And_Close_Converge_On_One_Completion()
    {
        var created = await Service.CreateAsync(Input(amount: "72.45"));
        var payment = Payment(created);
        var closeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseClose = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Fake.OnClose = async _ =>
        {
            closeStarted.TrySetResult();
            await releaseClose.Task;
            return new ChannelCloseResult(ChannelCloseOutcome.AlreadyPaid, payment);
        };
        Fake.QueryResults[created.CollectionNo] = new ChannelQueryResult(ChannelQueryOutcome.Paid, payment);

        var cancel = RunInScopeAsync(provider => Integration(provider).CancelAsync(created.Id));
        try
        {
            await closeStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var query = RunInScopeAsync(provider => Integration(provider).QueryChannelAsync(created.Id));
            var callbacks = Enumerable.Range(0, 3).Select(_ => RunInScopeAsync(provider =>
                provider.GetRequiredService<CollectionPaymentConfirmationService>().ConfirmAsync(payment))).ToArray();
            releaseClose.SetResult();

            var results = await Task.WhenAll(callbacks);
            results.Count(x => x.Outcome == CollectionPaymentConfirmationOutcome.Accepted).ShouldBeLessThanOrEqualTo(1);
            results.All(x => x.Outcome is CollectionPaymentConfirmationOutcome.Accepted
                or CollectionPaymentConfirmationOutcome.Duplicate).ShouldBeTrue();
            (await query).Status.ShouldBe(CollectionStatus.Completed);
            (await cancel).Collection.Status.ShouldBe(CollectionStatus.Completed);
            (await Service.GetAsync(created.Id))!.ChannelTradeNo.ShouldBe(payment.TradeNo);
            var duplicate = await GetRequiredService<CollectionPaymentConfirmationService>().ConfirmAsync(payment);
            duplicate.Outcome.ShouldBe(CollectionPaymentConfirmationOutcome.Duplicate);
            var completions = await ReadAsync(async () =>
            {
                var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
                return await db.Set<ColPaymentEvent>().CountAsync(item => item.Id == created.Id
                    && item.Kind == CollectionPaymentEventKind.Completed);
            });
            completions.ShouldBe(1);
        }
        finally
        {
            releaseClose.TrySetResult();
            await cancel;
        }
    }

    [Fact]
    public async Task A_Provider_Trade_Cannot_Complete_Two_Collections()
    {
        var first = await Service.CreateAsync(Input(amount: "72.45"));
        var second = await Service.CreateAsync(Input(amount: "72.45"));
        var results = await Task.WhenAll(new[] { first, second }.Select(row => RunInScopeAsync(provider =>
            provider.GetRequiredService<CollectionPaymentConfirmationService>().ConfirmAsync(Payment(row)))));

        results.Count(result => result.Outcome == CollectionPaymentConfirmationOutcome.Accepted).ShouldBe(1);
        results.Count(result => result.Outcome == CollectionPaymentConfirmationOutcome.Conflict).ShouldBe(1);
    }

    [Fact]
    public async Task Payment_Requires_The_Original_Merchant_Currency_And_Amount()
    {
        var created = await Service.CreateAsync(Input(amount: "72.45"));
        var payment = Payment(created);
        var service = GetRequiredService<CollectionPaymentConfirmationService>();
        foreach (var mismatch in new[]
        {
            payment with { MerchantAccountId = "wrong-merchant" },
            payment with { Channel = CollectionChannel.AlipayPrecreate },
            payment with { Amount = 72.46m },
            payment with { Currency = CollectionCurrency.Usd },
        })
        {
            (await service.ConfirmAsync(mismatch)).Outcome.ShouldBe(CollectionPaymentConfirmationOutcome.Conflict);
        }

        (await Service.GetAsync(created.Id))!.Status.ShouldBe(CollectionStatus.Pending);
        (await service.ConfirmAsync(payment)).Outcome.ShouldBe(CollectionPaymentConfirmationOutcome.Accepted);
    }

    [Fact]
    public async Task Unknown_Close_Holds_The_Original_Attempt_Until_Query_Confirms_It_Closed()
    {
        var input = Input(amount: "72.45");
        var created = await Service.CreateAsync(input);
        Fake.CloseResults[created.CollectionNo] = new ChannelCloseResult(ChannelCloseOutcome.Unknown);

        var unresolved = await Service.CancelAsync(created.Id);
        unresolved.Collection.Status.ShouldBe(CollectionStatus.Pending);
        unresolved.Collection.ChannelResultUnknown.ShouldBeTrue();
        await Should.ThrowAsync<BusinessException>(() => Service.CreateAsync(input));
        await Should.ThrowAsync<BusinessException>(() => Service.CreateAsync(
            Input("72.45", CollectionChannel.AlipayPrecreate, businessId: input.BusinessId)));

        Fake.QueryResults[created.CollectionNo] = new ChannelQueryResult(ChannelQueryOutcome.OrderNotFound);
        (await Service.QueryChannelAsync(created.Id)).Status.ShouldBe(CollectionStatus.Pending);

        Fake.QueryResults[created.CollectionNo] = new ChannelQueryResult(ChannelQueryOutcome.Closed);
        (await Service.QueryChannelAsync(created.Id)).Status.ShouldBe(CollectionStatus.Closed);
        var replacement = await Service.CreateAsync(
            Input("72.45", CollectionChannel.AlipayPrecreate, businessId: input.BusinessId));
        replacement.Id.ShouldNotBe(created.Id);
    }

    [Fact]
    public async Task Concurrent_Create_And_Close_Cannot_Expose_A_New_Payment_Payload()
    {
        var input = Input("72.45");
        var started = new TaskCompletionSource<ChannelCreateRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Fake.OnCreate = async request =>
        {
            started.TrySetResult(request);
            await resume.Task;
            return new ChannelCreateResult(Succeeded: true, Payload: "should-not-be-exposed");
        };

        var first = RunInScopeAsync(provider => Integration(provider).CreateAsync(input));
        try
        {
            var request = await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var duplicate = await Service.CreateAsync(input);
            duplicate.Id.ShouldBe(request.CollectionId);
            duplicate.ChannelPayload.ShouldBeNull();
            Fake.CreateCallCount.ShouldBe(1);

            var close = await Service.CancelAsync(request.CollectionId);
            close.Collection.Status.ShouldBe(CollectionStatus.Pending);
            close.Collection.ChannelResultUnknown.ShouldBeTrue();
            Fake.CloseCallCount.ShouldBe(0);
            Fake.QueryResults[request.CollectionNo] = new ChannelQueryResult(ChannelQueryOutcome.Closed);
            (await Service.QueryChannelAsync(request.CollectionId)).Status.ShouldBe(CollectionStatus.Pending);

            resume.SetResult();
            (await first).ChannelPayload.ShouldBeNull();
            (await Service.QueryChannelAsync(request.CollectionId)).Status.ShouldBe(CollectionStatus.Closed);
        }
        finally
        {
            resume.TrySetResult();
            await first;
        }
    }

    [Fact]
    public async Task Paid_Mismatch_Remains_Visible_And_Blocks_Replacement_Until_Resolved()
    {
        var input = Input("72.45");
        var created = await Service.CreateAsync(input);
        var payment = Payment(created);
        var confirmation = GetRequiredService<CollectionPaymentConfirmationService>();
        (await confirmation.ConfirmAsync(payment with { Amount = 71m })).Outcome
            .ShouldBe(CollectionPaymentConfirmationOutcome.Conflict);
        Fake.QueryResults[created.CollectionNo] = new ChannelQueryResult(ChannelQueryOutcome.Closed);
        var unresolved = await Service.QueryChannelAsync(created.Id);
        unresolved.Status.ShouldBe(CollectionStatus.Pending);
        unresolved.ChannelResultUnknown.ShouldBeTrue();
        unresolved.StatusNote!.ShouldContain("does not match");
        await Should.ThrowAsync<BusinessException>(() => Service.CreateAsync(input));

        (await confirmation.ConfirmAsync(payment)).Outcome.ShouldBe(CollectionPaymentConfirmationOutcome.Accepted);
        (await confirmation.ConfirmAsync(payment with { TradeNo = "other-paid-trade" })).Outcome
            .ShouldBe(CollectionPaymentConfirmationOutcome.Conflict);
        var completed = (await Service.GetAsync(created.Id))!;
        completed.Status.ShouldBe(CollectionStatus.Completed);
        completed.ChannelTradeNo.ShouldBe(payment.TradeNo);
        completed.StatusNote!.ShouldContain("other-paid-trade");
    }

    [Fact]
    public async Task Callback_Can_Complete_Before_Create_Channel_Response()
    {
        var prepared = false;
        Fake.OnCreate = async request =>
        {
            prepared.ShouldBeTrue();
            await RunInScopeAsync(async provider =>
            {
                var row = await Integration(provider).GetAsync(request.CollectionId);
                row.ShouldNotBeNull();
                var service = provider.GetRequiredService<CollectionPaymentConfirmationService>();
                (await service.ConfirmAsync(Payment(row))).Outcome.ShouldBe(CollectionPaymentConfirmationOutcome.Accepted);
                return true;
            });

            return new ChannelCreateResult(Succeeded: false, Unknown: true);
        };

        var created = await Service.CreateAsync(Input("72.45"), _ =>
        {
            prepared = true;
            return Task.CompletedTask;
        });
        created.Status.ShouldBe(CollectionStatus.Completed);
        created.ChannelResultUnknown.ShouldBeFalse();
    }

    private CollectionPaymentObservation Payment(CollectionDto row) =>
        new(row.Channel, Fake.MerchantAccountId, row.CollectionNo, "fake-paid", 72.45m, row.Currency);

    private static CollectionIntegrationService Integration(IServiceProvider provider) =>
        (CollectionIntegrationService)provider.GetRequiredService<ICollectionIntegrationService>();

    private async Task<T> RunInScopeAsync<T>(Func<IServiceProvider, Task<T>> run)
    {
        using var scope = ServiceProvider.CreateScope();
        return await run(scope.ServiceProvider);
    }

    [Fact]
    public async Task Amount_Passes_Through_When_Channel_Limit_Unconfigured()
    {
        // 未配置上限（默认空）= 不限（计划第 4.2 节：上限按渠道币种配置，空值不限）
        var created = await Service.CreateAsync(Input(amount: "999999.99"));
        created.Status.ShouldBe(CollectionStatus.Pending);
    }

    [Fact]
    public async Task Amount_Above_Configured_Channel_Limit_Is_Rejected()
    {
        await GetRequiredService<Volo.Abp.SettingManagement.ISettingManager>()
            .SetGlobalAsync(CollectionSettingsKeys.MaxAmountWechatNativeCny, "100");

        var exception = await Should.ThrowAsync<BusinessException>(
            () => Service.CreateAsync(Input(amount: "150")));
        exception.Code.ShouldBe(CollectionErrorCodes.AmountExceedsChannelLimit);
    }

    [Fact]
    public async Task Unparseable_Configured_Limit_Fails_Closed()
    {
        // 配置了但解析不出来：按「限制存在但读不出」拒绝，不能静默放开上限（fail-closed）
        await GetRequiredService<Volo.Abp.SettingManagement.ISettingManager>()
            .SetGlobalAsync(CollectionSettingsKeys.MaxAmountWechatNativeCny, "not-a-number");

        (await Should.ThrowAsync<BusinessException>(
                () => Service.CreateAsync(Input(amount: "50"))))
            .Code.ShouldBe(CollectionErrorCodes.AmountExceedsChannelLimit);
    }

    private static class CollectionSettingsKeys
    {
        public const string MaxAmountWechatNativeCny = "Collection.MaxAmount.WechatNative.Cny";
    }

    private async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        using var uow = ServiceProvider.GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>().Begin();
        var result = await read();
        await uow.CompleteAsync();
        return result;
    }

    private static CreateCollectionInput Input(
        string amount,
        CollectionChannel channel = CollectionChannel.WechatNative,
        DateTime? expiresAt = null,
        Guid? businessId = null)
    {
        return new CreateCollectionInput
        {
            BusinessType = "Order",
            BusinessId = businessId ?? Guid.NewGuid(),
            UserId = UserId,
            Channel = channel,
            Currency = channel == CollectionChannel.StripeCheckout ? CollectionCurrency.Usd : CollectionCurrency.Cny,
            Amount = amount,
            ExpiresAt = expiresAt ?? DateTime.UtcNow.AddHours(1),
        };
    }
}
