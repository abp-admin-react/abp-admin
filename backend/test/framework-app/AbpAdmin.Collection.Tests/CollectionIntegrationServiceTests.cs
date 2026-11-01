using System;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Fakes;
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
            ChannelCloseOutcome.AlreadyPaid, TradeNo: "fake-paid", PaidAmount: 72.45m);

        var result = await Service.CancelAsync(created.Id);

        // 升级路径：渠道返回已支付、金额核对一致 → 标 Completed（事件在提交后发布）
        result.Collection.Status.ShouldBe(CollectionStatus.Completed);
        result.Collection.CompletedAt.ShouldNotBeNull();
        result.Collection.ChannelTradeNo.ShouldNotBeNullOrEmpty();
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
