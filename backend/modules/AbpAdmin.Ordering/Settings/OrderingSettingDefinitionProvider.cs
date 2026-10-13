using AbpAdmin.Ordering.Localization;
using AbpAdmin.Settings;
using Volo.Abp.Localization;
using Volo.Abp.Settings;

namespace AbpAdmin.Ordering.Settings;

/// <summary>
/// 订单模块运行期参数定义。WithUi 走框架 SettingDefinitionUiExtensions（纯字符串参数，
/// 控件值与 EasyAbp SettingUi 约定一致的小写字面量）——订单工程不直接引用任何
/// EasyAbp 包（计划第 10 节）。Stripe 报价有效期夹在 30 分钟与 24 小时之间
///（QuoteManager 里钳制），Stripe 固定费默认不低于 0.30 USD。
/// </summary>
public class OrderingSettingDefinitionProvider : SettingDefinitionProvider
{
    // EasyAbp SettingUi 的控件类型值（text/number），与框架 AbpAdminSettingUiConsts.PasswordType 同款镜像
    private const string ComponentText = "text";
    private const string ComponentNumber = "number";

    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(
            Text(OrderingSettings.Quote.UsdCnyRate, OrderingSettings.Defaults.UsdCnyRate),
            Number(OrderingSettings.Quote.ValidityMinutes, OrderingSettings.Defaults.ValidityMinutes.ToString()),
            Text(OrderingSettings.Quote.WechatPercentRate, OrderingSettings.Defaults.DefaultPercentRate),
            Text(OrderingSettings.Quote.WechatFixedFee, OrderingSettings.Defaults.DefaultFixedFee),
            Text(OrderingSettings.Quote.WechatSettlementFloor, OrderingSettings.Defaults.DefaultSettlementFloor),
            Text(OrderingSettings.Quote.WechatFixedFeeRate, OrderingSettings.Defaults.DefaultFixedFeeRate),
            Text(OrderingSettings.Quote.AlipayPercentRate, OrderingSettings.Defaults.DefaultPercentRate),
            Text(OrderingSettings.Quote.AlipayFixedFee, OrderingSettings.Defaults.DefaultFixedFee),
            Text(OrderingSettings.Quote.AlipaySettlementFloor, OrderingSettings.Defaults.DefaultSettlementFloor),
            Text(OrderingSettings.Quote.AlipayFixedFeeRate, OrderingSettings.Defaults.DefaultFixedFeeRate),
            Text(OrderingSettings.Quote.StripePercentRate, OrderingSettings.Defaults.StripePercentRate),
            Text(OrderingSettings.Quote.StripeFixedFee, OrderingSettings.Defaults.StripeFixedFee),
            Text(OrderingSettings.Quote.StripeSettlementFloor, OrderingSettings.Defaults.DefaultSettlementFloor),
            Text(OrderingSettings.Quote.StripeFixedFeeRate, OrderingSettings.Defaults.DefaultFixedFeeRate),
            Text(OrderingSettings.Quote.BalanceUsdtPercentRate, OrderingSettings.Defaults.DefaultPercentRate),
            Text(OrderingSettings.Quote.BalanceUsdtFixedFee, OrderingSettings.Defaults.DefaultFixedFee),
            Text(OrderingSettings.Quote.BalanceUsdtSettlementFloor, OrderingSettings.Defaults.DefaultSettlementFloor),
            Text(OrderingSettings.Quote.BalanceUsdtFixedFeeRate, OrderingSettings.Defaults.DefaultFixedFeeRate),
            Text(OrderingSettings.Quote.BalanceUsdcPercentRate, OrderingSettings.Defaults.DefaultPercentRate),
            Text(OrderingSettings.Quote.BalanceUsdcFixedFee, OrderingSettings.Defaults.DefaultFixedFee),
            Text(OrderingSettings.Quote.BalanceUsdcSettlementFloor, OrderingSettings.Defaults.DefaultSettlementFloor),
            Text(OrderingSettings.Quote.BalanceUsdcFixedFeeRate, OrderingSettings.Defaults.DefaultFixedFeeRate),
            Number(OrderingSettings.Order.ExpiryHours, OrderingSettings.Defaults.ExpiryHours.ToString()));
    }

    private static SettingDefinition Text(string name, string defaultValue)
    {
        return new SettingDefinition(
            name,
            defaultValue,
            L("DisplayName:" + name),
            L("Description:" + name))
            .WithProviders(
                GlobalSettingValueProvider.ProviderName,
                DefaultValueSettingValueProvider.ProviderName)
            .WithUi(OrderingSettings.SettingUiGroup, group2: null, ComponentText);
    }

    private static SettingDefinition Number(string name, string defaultValue)
    {
        return new SettingDefinition(
            name,
            defaultValue,
            L("DisplayName:" + name),
            L("Description:" + name))
            .WithProviders(
                GlobalSettingValueProvider.ProviderName,
                DefaultValueSettingValueProvider.ProviderName)
            .WithUi(OrderingSettings.SettingUiGroup, group2: null, ComponentNumber);
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<OrderingResource>(name);
    }
}
