using AbpAdmin.Collection.Chain;
using Shouldly;
using Xunit;

namespace AbpAdmin.Collection;

public class BscRpcQuantityTests
{
    [Theory]
    [InlineData("0x0", "0")]
    [InlineData("0xf", "15")]
    [InlineData("0x123", "291")]
    [InlineData("0x1c6bf52634000", "500000000000000")]
    [InlineData("0xffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff",
        "115792089237316195423570985008687907853269984665640564039457584007913129639935")]
    public void Parses_Unsigned_Quantities_Of_Both_Lengths(string hex, string expected)
    {
        AnkrBscChainGateway.HexToDecimalString(hex).ShouldBe(expected);
    }

    [Fact]
    public void Minimum_Fuel_Credit_Keeps_Its_Whole_Value()
    {
        AnkrBscChainGateway.HexWeiToEther("0x1c6bf52634000").ShouldBe(0.0005m);
    }
}
