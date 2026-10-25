using AbpAdmin.Collection.Withdrawals;
using Shouldly;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>提现开关解析表驱动锁（Cursor 计划）：垃圾值/空一律 fail-closed 关。</summary>
public class WithdrawEnabledPredicateTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("garbage", false)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("TRUE", true)]
    public void Parses_Only_Boolean_True_As_Enabled(string? raw, bool expected)
    {
        WithdrawManager.IsEnabled(raw).ShouldBe(expected);
    }
}
