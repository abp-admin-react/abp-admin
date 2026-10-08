using System;
using System.Collections.Generic;
using System.Linq;
using AbpAdmin.Linq.DynamicQueryable;
using Shouldly;
using Xunit;

namespace AbpAdmin.DynamicQueryable.Tests;

/// <summary>
/// 动态查询表达式构建的纯单测（LINQ to Objects 执行，验证语义而非 SQL 翻译；
/// SQL 侧的正确性由使用方的集成测试承担——EF 的表达式翻译是同一表达式树）。
/// </summary>
public class ObjectQueryableExtensionsTests
{
    private sealed record Person(
        string UserName,
        int Age,
        bool IsActive,
        DateTime? LastLoginAt,
        PersonKind Kind,
        Guid TenantId);

    private enum PersonKind { Regular = 0, Admin = 1 }

    private static readonly List<Person> People = new()
    {
        new("alice", 30, true, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), PersonKind.Admin, Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d")),
        new("bob", 22, false, null, PersonKind.Regular, Guid.Parse("11111111-1111-1111-1111-111111111111")),
        new("carol", 41, true, new DateTime(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc), PersonKind.Regular, Guid.Parse("22222222-2222-2222-2222-222222222222")),
        new("dave", 22, true, null, PersonKind.Admin, Guid.Empty),
    };

    /// <summary>模拟调用方的可筛字段白名单（常规用例全字段放开；白名单拒绝语义由专测用窄白名单钉住）。</summary>
    private static readonly string[] SearchableFields =
    {
        "UserName", "Age", "IsActive", "LastLoginAt", "Kind", "TenantId",
    };

    private static IQueryable<Person> Apply(params DynamicCondition[] conditions)
        => People.AsQueryable().DynamicQuery(conditions, SearchableFields);

    private static DynamicCondition C(string field, DynamicComparison comparison, string? value = null, DynamicLogic logic = DynamicLogic.And)
        => new() { Field = field, Comparison = comparison, Value = value, Logic = logic };

    [Fact]
    public void Equal_On_String_And_Int()
    {
        Apply(C("UserName", DynamicComparison.Equal, "alice")).ShouldHaveSingleItem().UserName.ShouldBe("alice");
        Apply(C("Age", DynamicComparison.Equal, "22")).Count().ShouldBe(2);
    }

    [Fact]
    public void Contains_StartsWith_EndsWith()
    {
        Apply(C("UserName", DynamicComparison.Contains, "a")).Select(p => p.UserName).ShouldBe(new[] { "alice", "carol", "dave" });
        Apply(C("UserName", DynamicComparison.StartsWith, "a")).ShouldHaveSingleItem().UserName.ShouldBe("alice");
        Apply(C("UserName", DynamicComparison.EndsWith, "b")).ShouldHaveSingleItem().UserName.ShouldBe("bob");
        Apply(C("UserName", DynamicComparison.NotContains, "a")).Select(p => p.UserName).ShouldBe(new[] { "bob" });
    }

    [Fact]
    public void Ordered_Comparisons_On_Numbers_And_Nullable()
    {
        Apply(C("Age", DynamicComparison.GreaterThan, "30")).Select(p => p.UserName).ShouldBe(new[] { "carol" });
        Apply(C("Age", DynamicComparison.LessThanOrEqual, "22")).Count().ShouldBe(2);
        // 可空类型：HasValue 为假的不参与比较；alice 的登录时间恰等于边界值，不在 GreaterThan 结果内
        Apply(C("LastLoginAt", DynamicComparison.GreaterThan, "2026-01-01T00:00:00"))
            .Select(p => p.UserName)
            .ShouldBe(new[] { "carol" });
        Apply(C("LastLoginAt", DynamicComparison.GreaterThanOrEqual, "2026-01-01T00:00:00"))
            .Select(p => p.UserName)
            .ShouldBe(new[] { "alice", "carol" });
    }

    [Fact]
    public void Null_NotNull_Use_Default_Semantics()
    {
        Apply(C("LastLoginAt", DynamicComparison.Null)).Select(p => p.UserName).ShouldBe(new[] { "bob", "dave" });
        Apply(C("LastLoginAt", DynamicComparison.NotNull)).Select(p => p.UserName).ShouldBe(new[] { "alice", "carol" });
    }

    [Fact]
    public void Enum_Guid_Bool_Conversions()
    {
        Apply(C("Kind", DynamicComparison.Equal, "Admin")).Count().ShouldBe(2);
        Apply(C("Kind", DynamicComparison.Equal, "1")).Count().ShouldBe(2);
        Apply(C("TenantId", DynamicComparison.Equal, "22222222-2222-2222-2222-222222222222"))
            .ShouldHaveSingleItem().UserName.ShouldBe("carol");
        Apply(C("IsActive", DynamicComparison.Equal, "false")).ShouldHaveSingleItem().UserName.ShouldBe("bob");
    }

    [Fact]
    public void Logic_Combines_Accumulated_Branches()
    {
        // AND：所有条件相与
        Apply(
            C("Age", DynamicComparison.Equal, "22"),
            C("IsActive", DynamicComparison.Equal, "true")).ShouldHaveSingleItem().UserName.ShouldBe("dave");

        // OR：第二个条件声明 Or，与第一个相或
        Apply(
            C("UserName", DynamicComparison.Equal, "alice"),
            C("UserName", DynamicComparison.Equal, "bob", DynamicLogic.Or))
            .Select(p => p.UserName).ShouldBe(new[] { "alice", "bob" });
    }

    [Fact]
    public void Or_Then_And_Binds_To_Accumulated_Body()
    {
        // 累计语义：(Age=22) ∨ (UserName=alice)，再 ∧ (IsActive=true)
        // => ({bob,dave} ∪ {alice}) ∩ {alice,carol,dave} = {alice, dave}；
        // 若实现被误改成相邻两两结合 ((22∧alice)∨true) 则全员入选
        var result = Apply(
            C("Age", DynamicComparison.Equal, "22"),
            C("UserName", DynamicComparison.Equal, "alice", DynamicLogic.Or),
            C("IsActive", DynamicComparison.Equal, "true", DynamicLogic.And));
        result.Select(p => p.UserName).ShouldBe(new[] { "alice", "dave" });

        // 首条件的 Logic.Or 无效果（没有前值可结合）
        var firstOr = Apply(C("UserName", DynamicComparison.Equal, "alice", DynamicLogic.Or));
        firstOr.ShouldHaveSingleItem().UserName.ShouldBe("alice");
    }

    [Fact]
    public void Empty_Conditions_Return_Original_Source()
    {
        Apply().Count().ShouldBe(People.Count);
    }

    [Fact]
    public void Unknown_Field_Throws()
    {
        // 字段必须真实存在：fail-closed，不可能借字段名注入
        Should.Throw<ArgumentException>(() =>
            Apply(C("NotExists", DynamicComparison.Equal, "1")).ToList());
    }

    [Fact]
    public void Field_Outside_Whitelist_Throws_Even_If_Property_Exists()
    {
        // 安全契约内聚：TenantId 是真实存在的属性（PropertyOrField 可解析），
        // 但不在调用方白名单内即拒绝——防"借任意真实属性/导航链做筛选"的信息暴露面
        var exception = Should.Throw<ArgumentException>(() =>
            People.AsQueryable().DynamicQuery(
                new[] { C("TenantId", DynamicComparison.Equal, Guid.Empty.ToString()) },
                new[] { "UserName", "Age" }));
        exception.Message.ShouldContain("TenantId");
        exception.Message.ShouldContain("白名单");
    }

    [Fact]
    public void Whitelist_Match_Is_Case_Sensitive_Ordinal()
    {
        // 契约钉住：白名单按 Ordinal 精确匹配（与调用方 fieldMap 字典同语义）。
        // 若未来"顺手"换成 IgnoreCase，等于静默放大每个调用方的白名单——必须红
        Should.Throw<ArgumentException>(() =>
            People.AsQueryable().DynamicQuery(
                new[] { C("username", DynamicComparison.Equal, "alice") },
                new[] { "UserName" }));
    }

    [Fact]
    public void Null_Field_Throws_Argument_Not_NullReference()
    {
        // 契约钉住：Field=null 走白名单 Contains(null) 抛 ArgumentException（fail-closed），
        // 而不是落到表达式构建阶段变成别的异常形态（AppService 侧 400 映射依赖这个类型）
        Should.Throw<ArgumentException>(() =>
            People.AsQueryable().DynamicQuery(
                new[] { C(null!, DynamicComparison.Equal, "alice") },
                SearchableFields));
    }

    [Fact]
    public void Empty_Whitelist_Throws()
    {
        Should.Throw<ArgumentException>(() =>
            People.AsQueryable().DynamicQuery(
                new[] { C("UserName", DynamicComparison.Equal, "alice") },
                Array.Empty<string>()));
    }

    [Fact]
    public void Invalid_Value_Throws_Format_Error()
    {
        Should.Throw<Exception>(() =>
            Apply(C("Age", DynamicComparison.Equal, "not-a-number")).ToList());
    }
}
