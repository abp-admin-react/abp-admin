using AbpAdmin.Payments;
using EasyAbp.PaymentService;
using EasyAbp.PaymentService.Options;
using EasyAbp.PaymentService.Payments;
using EasyAbp.PaymentService.WeChatPay;
using Microsoft.Extensions.DependencyInjection;

namespace AbpAdmin.ModuleConfigurators;

/// <summary>
/// 支付渠道（Free / Prepayment / WeChatPay）配置（自 AbpAdminDomainModule 拆出，注册等价搬移）。
/// </summary>
internal static class PaymentsConfigurator
{
    public static void ConfigurePayments(this IServiceCollection services)
    {
        // T4.7：注册 Free / Prepayment / WeChatPay 渠道。0 元单走 Free，可在本地测通下单。
        services.Configure<PaymentServiceOptions>(options =>
        {
            options.Providers.Configure<FreePaymentServiceProvider>(FreePaymentServiceProvider.PaymentMethod);
            options.Providers.Configure<EasyAbp.PaymentService.Prepayment.PaymentService.PrepaymentPaymentServiceProvider>(
                EasyAbp.PaymentService.Prepayment.PaymentService.PrepaymentPaymentServiceProvider.PaymentMethod);
            options.Providers.Configure<WeChatPayPaymentServiceProvider>(WeChatPayPaymentServiceProvider.PaymentMethod);
        });

        services.Configure<EasyAbp.PaymentService.Prepayment.Options.PaymentServicePrepaymentOptions>(options =>
        {
            options.AccountGroups.Configure<DefaultPrepaymentAccountGroup>(accountGroup =>
            {
                accountGroup.Currency = "CNY";
            });
        });
    }
}
