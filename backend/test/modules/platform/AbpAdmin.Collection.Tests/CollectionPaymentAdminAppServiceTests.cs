using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;
using Xunit;

namespace AbpAdmin.Collection;

public class CollectionPaymentAdminAppServiceTests : CollectionTestBase
{
    [Fact]
    public async Task List_Filters_And_Pages_Collection_Records()
    {
        var now = DateTime.UtcNow;
        var older = Create("COL-001", CollectionChannel.WechatNative, CollectionStatus.Completed, now.AddDays(-1));
        var newer = Create("COL-002", CollectionChannel.WechatNative, CollectionStatus.Completed, now);
        var pending = Create("COL-003", CollectionChannel.WechatNative, CollectionStatus.Pending, now);
        var alipay = Create("COL-004", CollectionChannel.AlipayPrecreate, CollectionStatus.Completed, now);
        await InsertAsync(older, newer, pending, alipay);

        var service = GetRequiredService<CollectionPaymentAdminAppService>();
        var page = await service.GetListAsync(new CollectionPaymentListInput
        {
            Filter = " col- ",
            Channel = CollectionChannel.WechatNative,
            Status = CollectionStatus.Completed,
            SkipCount = 1,
            MaxResultCount = 1,
        });
        page.TotalCount.ShouldBe(2);
        page.Items.Single().Id.ShouldBe(older.Id);

        var matchingOrder = await service.GetListAsync(new CollectionPaymentListInput
        {
            Filter = pending.BusinessId.ToString(),
        });
        matchingOrder.Items.Single().Id.ShouldBe(pending.Id);

        var matchingChannelTrade = await service.GetListAsync(new CollectionPaymentListInput
        {
            Filter = "trade-col-004",
        });
        matchingChannelTrade.Items.Single().Id.ShouldBe(alipay.Id);
    }

    [Fact]
    public async Task Detail_Returns_Precise_Amounts_And_Rejects_Unknown_Ids()
    {
        var row = Create("COL-DETAIL", CollectionChannel.WechatNative, CollectionStatus.Pending, DateTime.UtcNow);
        row.Amount = 12.345678901234567891m;
        row.ChannelResultUnknown = true;
        row.StatusNote = "Pending query";
        row.ChannelPayload = "provider-payment-payload";
        await InsertAsync(row);

        var service = GetRequiredService<CollectionPaymentAdminAppService>();
        var detail = await service.GetAsync(row.Id);
        detail.CollectionNo.ShouldBe(row.CollectionNo);
        detail.Amount.ShouldBe("12.345678901234567891");
        detail.Currency.ShouldBe(nameof(CollectionCurrency.Cny));
        detail.Channel.ShouldBe(nameof(CollectionChannel.WechatNative));
        detail.Status.ShouldBe(nameof(CollectionStatus.Pending));
        detail.ChannelTradeNo.ShouldBe(row.ChannelTradeNo);
        detail.ChannelResultUnknown.ShouldBeTrue();
        detail.StatusNote.ShouldBe("Pending query");
        detail.GetType().GetProperty(nameof(ColCollection.ChannelPayload)).ShouldBeNull();
        await Should.ThrowAsync<EntityNotFoundException>(() => service.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Tenant_Cannot_Read_Host_Collections()
    {
        var service = GetRequiredService<CollectionPaymentAdminAppService>();
        using var tenant = GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid());

        var listError = await Should.ThrowAsync<BusinessException>(() =>
            service.GetListAsync(new CollectionPaymentListInput()));
        listError.Code.ShouldBe(CollectionErrorCodes.HostOnly);
        var detailError = await Should.ThrowAsync<BusinessException>(() => service.GetAsync(Guid.NewGuid()));
        detailError.Code.ShouldBe(CollectionErrorCodes.HostOnly);
    }

    private Task InsertAsync(params ColCollection[] rows) =>
        WithUnitOfWorkAsync(async () =>
        {
            var context = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            context.Collections.AddRange(rows);
            await context.SaveChangesAsync();
        });

    private static ColCollection Create(
        string number, CollectionChannel channel, CollectionStatus status, DateTime creationTime) => new(Guid.NewGuid())
    {
        CollectionNo = number,
        BusinessType = "Order",
        BusinessId = Guid.NewGuid(),
        UserId = Guid.NewGuid(),
        Channel = channel,
        Currency = CollectionCurrency.Cny,
        Amount = 10m,
        Status = status,
        ChannelTradeNo = $"TRADE-{number}",
        CreationTime = creationTime,
    };
}
