using System;
using System.Collections.Generic;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// P1 契约测试：批准数据字段集合（方案 4.3 + 审查项 a）。
/// 验证大小写敏感过滤、未批准键丢弃、空集合处理。
/// </summary>
public class ApprovedDataFieldSetTests
{
    /// <summary>
    /// 验证：Empty 集合过滤一切键。
    /// </summary>
    [Fact]
    public void Empty_Set_Should_Filter_Everything()
    {
        var set = ApprovedDataFieldSet.Empty;
        set.IsApproved("anyKey").ShouldBeFalse();

        var source = new Dictionary<string, object?>
        {
            ["foo"] = "bar",
            ["baz"] = 123,
        };
        var filtered = set.Filter(source);
        filtered.Count.ShouldBe(0);
    }

    /// <summary>
    /// 验证：大小写敏感比较（Ordinal），避免大小写变体逃脱检查。
    /// </summary>
    [Fact]
    public void Should_Use_Ordinal_Case_Sensitive_Comparison()
    {
        var set = new ApprovedDataFieldSet(["correlationId", "RetryAfterSeconds"]);

        set.IsApproved("correlationId").ShouldBeTrue();
        set.IsApproved("CorrelationId").ShouldBeFalse("Ordinal 大小写敏感");
        set.IsApproved("CORRELATIONID").ShouldBeFalse();

        set.IsApproved("RetryAfterSeconds").ShouldBeTrue();
        set.IsApproved("retryAfterSeconds").ShouldBeFalse();
    }

    /// <summary>
    /// 验证：Filter 丢弃未批准键，保留批准键。
    /// </summary>
    [Fact]
    public void Filter_Should_Drop_Unapproved_Keys()
    {
        var set = new ApprovedDataFieldSet(["approved1", "approved2"]);

        var source = new Dictionary<string, object?>
        {
            ["approved1"] = "value1",
            ["unapproved"] = "secret",
            ["approved2"] = 42,
            ["Approved1"] = "case-variant", // 大小写变体
        };

        var filtered = set.Filter(source);
        filtered.Count.ShouldBe(2);
        filtered["approved1"].ShouldBe("value1");
        filtered["approved2"].ShouldBe(42);
        filtered.ContainsKey("unapproved").ShouldBeFalse();
        filtered.ContainsKey("Approved1").ShouldBeFalse("大小写变体也应被丢弃");
    }

    /// <summary>
    /// 验证：Filter 处理 null 源返回空字典。
    /// </summary>
    [Fact]
    public void Filter_Null_Source_Should_Return_Empty()
    {
        var set = new ApprovedDataFieldSet(["key"]);
        var filtered = set.Filter(null);
        filtered.Count.ShouldBe(0);
    }

    /// <summary>
    /// 验证：Filter 处理空字典返回空字典。
    /// </summary>
    [Fact]
    public void Filter_Empty_Source_Should_Return_Empty()
    {
        var set = new ApprovedDataFieldSet(["key"]);
        var filtered = set.Filter(new Dictionary<string, object?>());
        filtered.Count.ShouldBe(0);
    }

    /// <summary>
    /// 验证：Filter 不修改原字典。
    /// </summary>
    [Fact]
    public void Filter_Should_Not_Modify_Source()
    {
        var set = new ApprovedDataFieldSet(["keep"]);
        var source = new Dictionary<string, object?>
        {
            ["keep"] = "value",
            ["drop"] = "secret",
        };

        var filtered = set.Filter(source);
        filtered.Count.ShouldBe(1);

        source.Count.ShouldBe(2, "原字典不应被修改");
        source["drop"].ShouldBe("secret");
    }

    /// <summary>
    /// 验证：构造时忽略 null/空字符串。
    /// </summary>
    [Fact]
    public void Constructor_Should_Ignore_Null_Or_Empty_Keys()
    {
        var set = new ApprovedDataFieldSet(["valid", "", "another"]);
        set.Fields.Count.ShouldBe(2);
        set.IsApproved("valid").ShouldBeTrue();
        set.IsApproved("another").ShouldBeTrue();
        set.IsApproved("").ShouldBeFalse();
    }

    /// <summary>
    /// 验证：Union 合并两个集合。
    /// </summary>
    [Fact]
    public void Union_Should_Merge_Two_Sets()
    {
        var set1 = new ApprovedDataFieldSet(["a", "b"]);
        var set2 = new ApprovedDataFieldSet(["b", "c"]);

        var merged = set1.Union(set2);
        merged.Fields.Count.ShouldBe(3);
        merged.IsApproved("a").ShouldBeTrue();
        merged.IsApproved("b").ShouldBeTrue();
        merged.IsApproved("c").ShouldBeTrue();
    }

    /// <summary>
    /// 验证：Fields 属性返回按 Ordinal 排序的快照。
    /// </summary>
    [Fact]
    public void Fields_Should_Be_Sorted_Snapshot()
    {
        var set = new ApprovedDataFieldSet(["zebra", "apple", "mango"]);
        set.Fields.ShouldBe(["apple", "mango", "zebra"], "应按 Ordinal 排序");
    }

    /// <summary>
    /// 验证：Filter 保留原值（包括 null）。
    /// </summary>
    [Fact]
    public void Filter_Should_Preserve_Values_Including_Null()
    {
        var set = new ApprovedDataFieldSet(["key1", "key2"]);

        var source = new Dictionary<string, object?>
        {
            ["key1"] = 123,
            ["key2"] = null,
        };

        var filtered = set.Filter(source);
        filtered["key1"].ShouldBe(123);
        filtered["key2"].ShouldBeNull();
    }
}
