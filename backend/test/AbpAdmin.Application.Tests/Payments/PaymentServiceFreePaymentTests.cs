using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAbp.PaymentService.Payments;
using EasyAbp.PaymentService.Payments.Dtos;
using Shouldly;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Payments;

public abstract class PaymentServiceFreePaymentTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    [Fact]
    public async Task Should_Create_And_Complete_Zero_Amount_Free_Payment()
    {
        var eventBus = GetRequiredService<IDistributedEventBus>();
        var paymentAppService = GetRequiredService<IPaymentAppService>();
        var itemKey = Guid.NewGuid().ToString("N");

        await WithUnitOfWorkAsync(() => eventBus.PublishAsync(new CreatePaymentEto(
            null,
            AdminUserId,
            FreePaymentServiceProvider.PaymentMethod,
            "CNY",
            new List<CreatePaymentItemEto>
            {
                new()
                {
                    ItemType = "Test",
                    ItemKey = itemKey,
                    OriginalPaymentAmount = 0m
                }
            })));

        var methods = await paymentAppService.GetListPaymentMethodAsync();
        methods.Items.ShouldContain(x => x.PaymentMethod == FreePaymentServiceProvider.PaymentMethod);

        var list = await paymentAppService.GetListAsync(new GetPaymentListInput
        {
            UserId = AdminUserId,
            MaxResultCount = 50
        });

        var created = list.Items.FirstOrDefault(x =>
            x.PaymentMethod == FreePaymentServiceProvider.PaymentMethod &&
            x.OriginalPaymentAmount == 0m);
        created.ShouldNotBeNull();

        var paid = await paymentAppService.PayAsync(created.Id, new PayInput());
        paid.CompletionTime.ShouldNotBeNull();
        paid.ActualPaymentAmount.ShouldBe(0m);
    }
}
