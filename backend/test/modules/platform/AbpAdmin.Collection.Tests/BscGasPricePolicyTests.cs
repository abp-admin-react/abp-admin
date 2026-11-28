using System;
using System.Numerics;
using AbpAdmin.Collection.Chain;
using Shouldly;
using Xunit;

namespace AbpAdmin.Collection;

public class BscGasPricePolicyTests
{
    [Fact]
    public void ApplyBump_At_Low_Bsc_GasPrice_Is_Strictly_Monotonic_And_Caps_Mult_After_MaxBumpSteps()
    {
        var base005Gwei = BscGasPricePolicy.GweiToWei(0.05m); // 50_000_000 wei
        base005Gwei.ShouldBe(new BigInteger(50_000_000));

        BscGasPricePolicy.ApplyBump(base005Gwei, 0).ShouldBe(base005Gwei);

        var prev = base005Gwei;
        for (var step = 1; step <= 10; step++)
        {
            var bumped = BscGasPricePolicy.ApplyBump(base005Gwei, step);
            bumped.ShouldBeGreaterThan(prev, $"step {step} 必须严格高于 step {step - 1}");
            prev = bumped;
        }

        // step 9 与 step 10 乘性停在 MaxBumpSteps=8，但仍 +step wei 保持不同交易哈希
        var s8 = BscGasPricePolicy.ApplyBump(base005Gwei, 8);
        var s9 = BscGasPricePolicy.ApplyBump(base005Gwei, 9);
        var s10 = BscGasPricePolicy.ApplyBump(base005Gwei, 10);
        (s9 - s8).ShouldBe(BigInteger.One);
        (s10 - s9).ShouldBe(BigInteger.One);
    }

    [Fact]
    public void ApplyBump_Tiny_Wei_Still_Increases_Strictly()
    {
        var oneWei = new BigInteger(1);
        BscGasPricePolicy.ApplyBump(oneWei, 1).ShouldBe(new BigInteger(2));
        BscGasPricePolicy.ApplyBump(oneWei, 2).ShouldBeGreaterThan(new BigInteger(2));
        Should.Throw<ArgumentOutOfRangeException>(() => BscGasPricePolicy.ApplyBump(new BigInteger(-1), 1));
    }

    [Fact]
    public void ReplacementGasWei_Respects_Ten_Percent_Plus_One_Floor()
    {
        var prev = BscGasPricePolicy.GweiToWei(1m);
        var lowDesired = BscGasPricePolicy.GweiToWei(1.05m);
        BscGasPricePolicy.ReplacementGasWei(lowDesired, prev)
            .ShouldBe(prev * 110 / 100 + 1);

        var highDesired = BscGasPricePolicy.GweiToWei(1.5m);
        BscGasPricePolicy.ReplacementGasWei(highDesired, prev)
            .ShouldBe(highDesired);

        BscGasPricePolicy.ReplacementGasWei(lowDesired, BigInteger.Zero)
            .ShouldBe(lowDesired);
    }

    [Fact]
    public void FuelBudgetGasPriceWei_Matches_MaxBumpSteps()
    {
        var net = BscGasPricePolicy.GweiToWei(1m);
        BscGasPricePolicy.FuelBudgetGasPriceWei(net)
            .ShouldBe(BscGasPricePolicy.ApplyBump(net, BscGasPricePolicy.MaxBumpSteps));
    }

    [Theory]
    [InlineData(1_000_000_000, 0, 100_000, 0)]
    [InlineData(1_000_000_000, 99_999, 100_000, 0)]
    [InlineData(1_000_000_000, 50_000_000_000_000, 100_000, 500_000_000)]
    [InlineData(1_000_000_000, 100_000_000_000_000, 100_000, 1_000_000_000)]
    [InlineData(1_000_000_000, 200_000_000_000_000, 100_000, 1_000_000_000)]
    public void CapToAffordable_Computes_Exact_Upper_Bound(
        long desiredWei, long balanceWei, int gasLimit, long expectedWei)
    {
        BscGasPricePolicy.CapToAffordable(desiredWei, balanceWei, gasLimit)
            .ShouldBe(new BigInteger(expectedWei));
    }

    [Fact]
    public void DepositScanWindowController_Halves_On_Incomplete_And_Recovers_Additively()
    {
        var ctl = new DepositScanWindowController();
        var tokenId = Guid.NewGuid();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();

        ctl.GetWindow(tokenId, p1, 1000).ShouldBe(1000);
        ctl.RecordIncomplete(tokenId, p1, 1000, 1000);
        ctl.GetWindow(tokenId, p1, 1000).ShouldBe(500);
        // 另一 provider 隔离不受影响
        ctl.GetWindow(tokenId, p2, 1000).ShouldBe(1000);

        ctl.RecordIncomplete(tokenId, p1, 1, 1000);
        ctl.GetWindow(tokenId, p1, 1000).ShouldBe(1);

        // 非整窗完成不放大；整窗完成后逐步放大并封顶于 configuredMax
        ctl.RecordComplete(tokenId, p1, 100, 200, 1000);
        ctl.GetWindow(tokenId, p1, 1000).ShouldBe(1);

        ctl.RecordComplete(tokenId, p1, 1, 1, 1000);
        ctl.GetWindow(tokenId, p1, 1000).ShouldBe(2);

        ctl.RecordComplete(tokenId, p1, 980, 980, 1000);
        ctl.GetWindow(tokenId, p1, 1000).ShouldBe(1000);
    }
}
