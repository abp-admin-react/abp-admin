using System.Globalization;
using AbpAdmin.Collection.Chain;
using Shouldly;
using Xunit;

namespace AbpAdmin.Collection;

public class ChainAmountTests
{
    [Theory]
    [InlineData("000", 255, "0")]
    [InlineData("00042", 6, "0.000042")]
    [InlineData("9999999999", 0, "9999999999")]
    [InlineData("9999999999999999", 6, "9999999999.999999")]
    [InlineData("9999999999999999999999999999", 18, "9999999999.999999999999999999")]
    [InlineData("99999999999999999999999999990", 19, "9999999999.999999999999999999")]
    [InlineData("10", 19, "0.000000000000000001")]
    public void Scales_Representable_Unsigned_Units_Exactly(string raw, int decimals, string expected)
    {
        DepositScanManager.TryScaleRaw(raw, decimals, out var overflow).ShouldBe(decimal.Parse(expected, CultureInfo.InvariantCulture));
        overflow.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null, 18)]
    [InlineData("", 18)]
    [InlineData(" 1", 18)]
    [InlineData("-1", 0)]
    [InlineData("+1", 18)]
    [InlineData("1.5", 18)]
    [InlineData("1e3", 18)]
    [InlineData("１２", 18)]
    [InlineData("1\0", 18)]
    [InlineData("0", -1)]
    [InlineData("1", int.MaxValue)]
    [InlineData("10000000000", 0)]
    [InlineData("10000000000000000", 6)]
    [InlineData("10000000000000000000000000000", 18)]
    [InlineData("1", 19)]
    [InlineData("100000000000000000000000000001", 20)]
    public void Rejects_Invalid_Or_Unrepresentable_Amounts_Without_Rounding(string? raw, int decimals)
    {
        DepositScanManager.TryScaleRaw(raw, decimals, out var overflow).ShouldBeNull();
        overflow.ShouldBeTrue();
    }
}
