namespace AbpAdmin.Ordering.Settings;

/// <summary>
/// 订单模块运行期参数名（第③层：设置页可调、免重启）。报价费率 S/F/f/r 全部来自配置，
/// 不写死 0.971 或 0.029（计划第 4.2 节）。设置项后改，已生成的报价不变。
/// </summary>
public static class OrderingSettings
{
    public const string Prefix = "Ordering";

    /// <summary>本模块设置页一级 Tab 名（SettingUi Group1 元数据值）。</summary>
    public const string SettingUiGroup = Prefix;

    public static class Quote
    {
        /// <summary>美元/人民币汇率（十进制字符串）。微信、支付宝报价用。</summary>
        public const string UsdCnyRate = Prefix + ".Quote.UsdCnyRate";

        /// <summary>报价有效期（分钟，不少于 15）。</summary>
        public const string ValidityMinutes = Prefix + ".Quote.ValidityMinutes";

        public const string WechatPercentRate = Prefix + ".Quote.Wechat.PercentRate";
        public const string WechatFixedFee = Prefix + ".Quote.Wechat.FixedFee";
        public const string WechatSettlementFloor = Prefix + ".Quote.Wechat.SettlementFloor";
        public const string WechatFixedFeeRate = Prefix + ".Quote.Wechat.FixedFeeRate";

        public const string AlipayPercentRate = Prefix + ".Quote.Alipay.PercentRate";
        public const string AlipayFixedFee = Prefix + ".Quote.Alipay.FixedFee";
        public const string AlipaySettlementFloor = Prefix + ".Quote.Alipay.SettlementFloor";
        public const string AlipayFixedFeeRate = Prefix + ".Quote.Alipay.FixedFeeRate";

        public const string StripePercentRate = Prefix + ".Quote.Stripe.PercentRate";
        public const string StripeFixedFee = Prefix + ".Quote.Stripe.FixedFee";
        public const string StripeSettlementFloor = Prefix + ".Quote.Stripe.SettlementFloor";
        public const string StripeFixedFeeRate = Prefix + ".Quote.Stripe.FixedFeeRate";

        public const string BalanceUsdtPercentRate = Prefix + ".Quote.BalanceUsdt.PercentRate";
        public const string BalanceUsdtFixedFee = Prefix + ".Quote.BalanceUsdt.FixedFee";
        public const string BalanceUsdtSettlementFloor = Prefix + ".Quote.BalanceUsdt.SettlementFloor";
        public const string BalanceUsdtFixedFeeRate = Prefix + ".Quote.BalanceUsdt.FixedFeeRate";

        public const string BalanceUsdcPercentRate = Prefix + ".Quote.BalanceUsdc.PercentRate";
        public const string BalanceUsdcFixedFee = Prefix + ".Quote.BalanceUsdc.FixedFee";
        public const string BalanceUsdcSettlementFloor = Prefix + ".Quote.BalanceUsdc.SettlementFloor";
        public const string BalanceUsdcFixedFeeRate = Prefix + ".Quote.BalanceUsdc.FixedFeeRate";
    }

    public static class Order
    {
        /// <summary>业务单待支付时限（小时），创建时写入订单的 ExpiresAt。</summary>
        public const string ExpiryHours = Prefix + ".Order.ExpiryHours";
    }

    /// <summary>定义默认值（与 SettingDefinitionProvider 单源一致，fail-closed 兜底读同一个常量）。</summary>
    public static class Defaults
    {
        public const string UsdCnyRate = "7.2";

        /// <summary>UsdCnyRate 的十进制兜底值（GetDecimalAsync 用，避免散落 decimal.Parse）。</summary>
        public const decimal UsdCnyRateValue = 7.2m;

        /// <summary>报价有效期默认 15 分钟（计划：默认不少于 15 分钟）。</summary>
        public const int ValidityMinutes = 15;

        /// <summary>Stripe 固定费默认不低于 0.30 USD（计划第 4.2 节）。</summary>
        public const string StripeFixedFee = "0.30";

        /// <summary>Stripe 比例费率默认值（单源常量，不在 Provider 里写死字面量）。</summary>
        public const string StripePercentRate = "0.029";

        public const int ExpiryHours = 24;

        public const string DefaultPercentRate = "0.006";

        /// <summary>DefaultPercentRate 的十进制兜底值。</summary>
        public const decimal DefaultPercentRateValue = 0.006m;

        public const string DefaultFixedFee = "0";
        public const string DefaultSettlementFloor = "0";
        public const string DefaultFixedFeeRate = "0";
    }
}
