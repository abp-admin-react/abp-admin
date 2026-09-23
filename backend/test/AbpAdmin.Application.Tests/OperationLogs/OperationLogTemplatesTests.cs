using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using AbpAdmin.Posts;
using Shouldly;
using Xunit;

namespace AbpAdmin.OperationLogs;

public class OperationLogTemplatesTests
{
    private static Dictionary<string, object?> Args(params (string key, object? value)[] pairs)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var (key, value) in pairs)
        {
            dict[key] = value;
        }

        return dict;
    }

    private sealed class StubParseFunction(string name, string? result) : IOperationLogParseFunction
    {
        public string Name { get; } = name;

        public ValueTask<string?> ResolveAsync(object? value) => new(result);
    }

    private sealed class ThrowingParseFunction : IOperationLogParseFunction
    {
        public string Name => "boom";

        public ValueTask<string?> ResolveAsync(object? value) => throw new InvalidOperationException("解析函数故障");
    }

    [Fact]
    public async Task Should_Resolve_Argument_And_Nested_Property()
    {
        var args = Args(("input", new { Name = "董事长", Code = "CEO" }));
        (await OperationLogTemplates.RenderAsync("创建了岗位「{{input.name}}」（{{input.code}}）", args))
            .ShouldBe("创建了岗位「董事长」（CEO）");
    }

    [Fact]
    public async Task Should_Resolve_Return_Value_And_Case_Insensitive_Root()
    {
        var args = Args(("id", Guid.Empty));
        (await OperationLogTemplates.RenderAsync("菜单 {{_ret.id}} / {{Id}}", args, returnValue: new { Id = "menu-1" }))
            .ShouldBe("菜单 menu-1 / 00000000-0000-0000-0000-000000000000");
    }

    [Fact]
    public async Task Should_Render_Special_Tokens_And_Null_As_Empty()
    {
        var args = Args(("missing", (object?)null));
        (await OperationLogTemplates.RenderAsync(
                "{{_type}}|{{_subType}}|{{_error}}|{{missing.deep}}",
                args,
                errorMessage: "boom",
                type: "身份管理",
                subType: "锁定用户"))
            .ShouldBe("身份管理|锁定用户|boom|");
    }

    [Fact]
    public async Task Should_Keep_Unresolvable_Token_As_Is()
    {
        (await OperationLogTemplates.RenderAsync("操作 {{nope}} 完成", Args()))
            .ShouldBe("操作 {{nope}} 完成");
    }

    [Fact]
    public async Task Should_Render_Bool_And_Enumerable()
    {
        var args = Args(("isEnabled", true), ("userIds", new List<Guid> { Guid.Parse("10000000-0000-0000-0000-000000000000"), Guid.Parse("20000000-0000-0000-0000-000000000000") }));
        (await OperationLogTemplates.RenderAsync("启用={{isEnabled}} 用户={{userIds}}", args))
            .ShouldBe("启用=是 用户=10000000-0000-0000-0000-000000000000,20000000-0000-0000-0000-000000000000");
    }

    [Fact]
    public void Diff_Should_Use_OperationLogField_DisplayNames()
    {
        var oldDto = new UpdatePostDto { Name = "旧岗位", Code = "OLD", SortOrder = 1, Status = PostStatusEnum.Enabled };
        var newDto = new UpdatePostDto { Name = "新岗位", Code = "OLD", SortOrder = 2, Status = PostStatusEnum.Enabled };

        OperationLogDiffer.Diff(oldDto, newDto)
            .ShouldBe("【岗位名称】旧岗位 → 新岗位；【显示顺序】1 → 2");
    }

    [Fact]
    public void Diff_Should_Return_Null_When_Equal_And_Handle_Scalars()
    {
        OperationLogDiffer.Diff(new { A = 1 }, new { A = 1 }).ShouldBeNull();
        OperationLogDiffer.Diff((object?)null, (object?)"x").ShouldBe("(空) → x");
        OperationLogDiffer.Diff(true, false).ShouldBe("是 → 否");
    }

    [Fact]
    public async Task Render_Should_Resolve_Diff_From_Scope_Variables()
    {
        try
        {
            OperationLogScope.Begin();
            OperationLogScope.Set("old", new UpdatePostDto { Name = "A", Code = "A1", SortOrder = 0, Status = PostStatusEnum.Enabled });
            var input = new UpdatePostDto { Name = "B", Code = "A1", SortOrder = 0, Status = PostStatusEnum.Enabled };

            (await OperationLogTemplates.RenderAsync("更新：{{_diff(old,input)}}", Args(("input", input))))
                .ShouldBe("更新：【岗位名称】A → B");
        }
        finally
        {
            OperationLogScope.Begin().Clear();
        }
    }

    [Fact]
    public async Task Render_Null_Template_Returns_Null()
    {
        (await OperationLogTemplates.RenderAsync(null, Args())).ShouldBeNull();
    }

    [Fact]
    public async Task Diff_With_Unresolvable_Operand_Keeps_Raw_Token()
    {
        var input = new UpdatePostDto { Name = "B", Code = "B1", SortOrder = 0, Status = PostStatusEnum.Enabled };
        (await OperationLogTemplates.RenderAsync("{{_diff(missing,input)}}", Args(("input", input))))
            .ShouldBe("{{_diff(missing,input)}}");
    }

    [Fact]
    public async Task Diff_With_Equal_Values_Renders_No_Change()
    {
        (await OperationLogTemplates.RenderAsync("{{_diff(a,b)}}", Args(
            ("a", "same"),
            ("b", "same")))).ShouldBe("（无变化）");
    }

    [Fact]
    public async Task Scope_Variables_Are_Visible_Across_Await_Boundary()
    {
        // 复刻 filter 场景：filter Begin → (await) 服务体内 Set → (await) 回到 filter 渲染。
        // 若 Set 被改成整体替换 Current.Value（AsyncLocal 值替换不反向传播），本测试会失败。
        try
        {
            OperationLogScope.Begin();
            await Task.Yield();

            await Task.Run(() => OperationLogScope.Set("old", "原始值"));
            await Task.Yield();

            (await OperationLogTemplates.RenderAsync("{{old}}", Args())).ShouldBe("原始值");
        }
        finally
        {
            OperationLogScope.Begin().Clear();
        }
    }

    [Fact]
    public async Task Function_Token_Should_Resolve_Via_Registered_Function()
    {
        var functions = new Dictionary<string, IOperationLogParseFunction>(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = new StubParseFunction("user", "zhangsan"),
        };

        (await OperationLogTemplates.RenderAsync("锁定了用户 {{user(id)}}", Args(("id", Guid.Empty)), functions))
            .ShouldBe("锁定了用户 zhangsan");
    }

    [Fact]
    public async Task Function_Name_Should_Be_Case_Insensitive_And_Support_Property_Path()
    {
        var functions = new Dictionary<string, IOperationLogParseFunction>(StringComparer.Ordinal)
        {
            ["user"] = new StubParseFunction("user", "zhangsan"),
        };

        (await OperationLogTemplates.RenderAsync("{{USER(input.userId)}}", Args(("input", new { UserId = Guid.Empty })), functions))
            .ShouldBe("zhangsan");
    }

    [Fact]
    public async Task Function_Token_With_Unresolvable_Argument_Keeps_Raw_Token()
    {
        var functions = new Dictionary<string, IOperationLogParseFunction>
        {
            ["user"] = new StubParseFunction("user", "zhangsan"),
        };

        (await OperationLogTemplates.RenderAsync("{{user(missing)}}", Args(), functions))
            .ShouldBe("{{user(missing)}}");
    }

    [Fact]
    public async Task Unknown_Function_Token_Keeps_Raw_Token()
    {
        // 未注册的函数名按普通路径解析：解析不到 → 原样保留（模板拼写错误可见）
        (await OperationLogTemplates.RenderAsync("{{ghost(id)}}", Args(("id", Guid.Empty)), new Dictionary<string, IOperationLogParseFunction>()))
            .ShouldBe("{{ghost(id)}}");
    }

    [Fact]
    public async Task Function_Null_Result_Renders_Empty_And_Thrown_Exception_Keeps_Raw_Token()
    {
        var functions = new Dictionary<string, IOperationLogParseFunction>(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = new StubParseFunction("user", null),
            ["boom"] = new ThrowingParseFunction(),
        };

        (await OperationLogTemplates.RenderAsync("[{{user(id)}}]", Args(("id", Guid.Empty)), functions))
            .ShouldBe("[]");
        (await OperationLogTemplates.RenderAsync("{{boom(id)}}", Args(("id", Guid.Empty)), functions))
            .ShouldBe("{{boom(id)}}");
    }

    [Fact]
    public async Task Function_Token_Should_Not_Steal_Diff_Token()
    {
        // _diff 也是「名字(参数)」形状：未注册为解析函数，必须仍走差异分支
        var functions = new Dictionary<string, IOperationLogParseFunction>(StringComparer.OrdinalIgnoreCase)
        {
            ["user"] = new StubParseFunction("user", "zhangsan"),
        };

        (await OperationLogTemplates.RenderAsync("{{_diff(a,b)}}", Args(("a", "x"), ("b", "y")), functions))
            .ShouldBe("x → y");
    }
}
