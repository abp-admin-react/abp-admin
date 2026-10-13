using AbpAdmin.Ordering.Quotes;
using Shouldly;
using Xunit;

namespace AbpAdmin.Ordering;

/// <summary>
/// X_min 表驱动测试（计划第 4.2、8 节）：分母小于等于 0 拒绝保存；分子小于等于 0 则没有下限；
/// 正常参数得到正的下限。微信、支付宝的 X_min 单位是 CNY，与订单金额比较时用
/// Amount × UsdCnyRate；Stripe 和余额的 X_min 与订单金额同单位直接比较
///（不能拿 CNY 下限直接和 USD 金额比——报价基数测试钉住这条）。
/// </summary>
public class QuoteFeeMathTests
{
    [Theory]
    [InlineData("0.971", "0.01", "0", "0.006", true, "160.17")]  // 正常参数 → 正的下限
    [InlineData("0.971", "0.30", "0", "0.029", true, "23.14")]   // Stripe 形态（S 为 USD 结算底线）
    [InlineData("0", "0.30", "0", "0.029", true, null)]          // 分子 ≤ 0 → 没有下限
    [InlineData("0.001", "5", "0", "0.029", true, null)]        // 分子 = 0.001 − 5 < 0 → 没有下限
    [InlineData("0.971", "0.01", "0", "0", false, null)]         // 分母 = 0 → 拒绝保存
    [InlineData("0.971", "0.01", "1", "0.006", false, null)]     // 分母 < 0（f=1）→ 拒绝保存
    [InlineData("0.971", "0.01", "0.5", "-0.01", false, null)]   // f/r 组合使分母 < 0 → 拒绝保存
    public void TryComputeXMin_Table(string s, string f, string feeRate, string r, bool expectedOk, string? expectedXMin)
    {
        var ok = QuoteFeeMath.TryComputeXMin(
            decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(f, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(feeRate, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(r, System.Globalization.CultureInfo.InvariantCulture),
            out var xMin, out var error);

        ok.ShouldBe(expectedOk);
        if (!expectedOk)
        {
            error.ShouldNotBeNullOrWhiteSpace(); // 拒绝保存并给出错误说明
            xMin.ShouldBeNull();
        }
        else if (expectedXMin == null)
        {
            error.ShouldBeNull();
            xMin.ShouldBeNull(); // 分子小于等于 0：该方式没有金额下限
        }
        else
        {
            error.ShouldBeNull();
            xMin.ShouldNotBeNull();
            xMin.Value.ShouldBe(decimal.Parse(expectedXMin, System.Globalization.CultureInfo.InvariantCulture));
            xMin.Value.ShouldBeGreaterThan(0m);
        }
    }

    [Fact]
    public void Wechat_PayAmount_Multiplies_UsdCnyRate_Stripe_Does_Not()
    {
        // 报价基数：微信/支付宝 = Amount × UsdCnyRate（CNY）；Stripe/余额 = Amount 本身
        var usd = 10m;
        var rate = 7.2m;

        var wechat = QuoteFeeMath.ComputePayAmount(usd * rate, 0.006m, 0.01m);
        var stripe = QuoteFeeMath.ComputePayAmount(usd, 0.029m, 0.30m);

        wechat.ShouldBe(72.45m); // ceil(72 × 1.006 + 0.01, 0.01) = ceil(72.442) → 72.45
        stripe.ShouldBe(10.59m); // ceil(10 × 1.029 + 0.30, 0.01) = ceil(10.59) → 10.59
    }

    [Fact]
    public void ComputePayAmount_Ceils_To_Cents()
    {
        QuoteFeeMath.ComputePayAmount(10m, 0.029m, 0.30m).ShouldBe(10.59m);     // 恰好两位小数，不进位
        QuoteFeeMath.ComputePayAmount(10.001m, 0.029m, 0.30m).ShouldBe(10.60m); // 10.591029 → 进位
        QuoteFeeMath.ComputePayAmount(72m, 0.006m, 0.01m).ShouldBe(72.45m);     // 72.442 → 进位
    }
}
