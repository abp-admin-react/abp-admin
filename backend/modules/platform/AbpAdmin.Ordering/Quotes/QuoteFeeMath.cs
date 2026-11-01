using System;

namespace AbpAdmin.Ordering.Quotes;

/// <summary>
/// 报价费率数学（计划第 4.2 节）：
/// <code>X_min = (S − (1 − f) × F) / ((1 − f) × (1 + r) − 1)</code>
/// S = 结算底线（SettlementFloor）、F = 固定费、f = 固定费抽取、r = 比例费率，全部来自配置。
/// 分母小于等于 0 → 拒绝保存；分子小于等于 0 → 该方式没有金额下限。
/// 微信、支付宝的 X_min 单位是 CNY，与订单金额比较时用 Amount × UsdCnyRate；
/// Stripe 和余额的 X_min 单位与订单金额相同，直接比较（表驱动测试钉住，计划第 8 节）。
/// </summary>
public static class QuoteFeeMath
{
    public static bool TryComputeXMin(
        decimal settlementFloor,
        decimal fixedFee,
        decimal fixedFeeRate,
        decimal percentRate,
        out decimal? xMin,
        out string? error)
    {
        xMin = null;
        error = null;

        var denominator = (1m - fixedFeeRate) * (1m + percentRate) - 1m;
        if (denominator <= 0m)
        {
            error = "X_min denominator ((1 - f) × (1 + r) - 1) must be greater than zero.";
            return false;
        }

        var numerator = settlementFloor - (1m - fixedFeeRate) * fixedFee;
        if (numerator <= 0m)
        {
            // 分子小于等于 0：该方式没有金额下限
            return true;
        }

        xMin = decimal.Round(numerator / denominator, 2, MidpointRounding.ToEven);
        return true;
    }

    /// <summary>
    /// 报价金额公式（计划第 4.2 节表格）：Ceil(基数 × (1 + r) + F, 0.01)——
    /// 微信/支付宝的基数是 Amount × UsdCnyRate（CNY）；Stripe 与余额的基数就是 Amount
    ///（Stripe 为 USD；余额为对应稳定币，1 单位按 1 USD）。
    /// </summary>
    public static decimal ComputePayAmount(decimal basis, decimal percentRate, decimal fixedFee)
    {
        var raw = basis * (1m + percentRate) + fixedFee;
        return Math.Ceiling(raw * 100m) / 100m;
    }
}
