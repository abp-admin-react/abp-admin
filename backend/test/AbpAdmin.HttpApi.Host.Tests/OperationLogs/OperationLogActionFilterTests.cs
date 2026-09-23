using System;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace AbpAdmin.OperationLogs;

/* OperationLogActionFilter 契约测试（审查 test 透镜 F3：模板引擎唯一消费者零覆盖）。
 * 覆盖：方法级注解渲染、失败模板（executed.Exception / next() 抛出两种路径）、
 * 类级兜底、无注解直通、writer 故障不传染业务响应、解析函数/关联 ID 透传。
 */
public class OperationLogActionFilterTests
{
    private const string TestCorrelationId = "filter-test-correlation-id";

    private sealed class StubController
    {
        [OperationLog("测试", "创建", BizNo = "{{id}}", Success = "创建了 {{name}}（{{_ret.value}}）")]
        public StubResult Create(Guid id, string name) => new() { Value = "rv" };

        [OperationLog("测试", "删除", BizNo = "{{id}}", Success = "删除了 {{id}}", Fail = "删除 {{id}} 失败：{{_error}}")]
        public void Delete(Guid id) => throw new InvalidOperationException("业务炸了");

        [OperationLog("测试", "返回错误码", Success = "ok")]
        public IActionResult Conflict() => new ConflictResult();

        [OperationLog("测试", "锁定", BizNo = "{{id}}", Success = "锁定了用户 {{user(id)}}")]
        public void Lock(Guid id) { }

        public void NoAttribute() { }
    }

    [OperationLog("测试", "类级兜底", Success = "类级注解生效")]
    private sealed class ClassLevelController
    {
        public void Ping() { }
    }

    private sealed class StubResult
    {
        public string Value { get; set; } = "";
    }

    private sealed class RecordingWriter : IOperationLogWriter
    {
        public List<OperationLogEntry> Entries { get; } = new();

        public Task WriteAsync(OperationLogEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>固定值替身：验证 filter 从 ICorrelationIdProvider 取值（与 ABP AuditLog 同源）。</summary>
    private sealed class FakeCorrelationIdProvider : Volo.Abp.Tracing.ICorrelationIdProvider
    {
        public string? Get() => TestCorrelationId;

        public IDisposable Change(string? correlationId) => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class StubParseFunction(string name, string? result) : IOperationLogParseFunction
    {
        public string Name { get; } = name;

        public ValueTask<string?> ResolveAsync(object? value) => new(result);
    }

    private static (OperationLogActionFilter Filter, RecordingWriter Writer) Build(
        params IOperationLogParseFunction[] parseFunctions)
    {
        var writer = new RecordingWriter();
        return (new OperationLogActionFilter(
            writer,
            new FakeCorrelationIdProvider(),
            parseFunctions,
            NullLogger<OperationLogActionFilter>.Instance), writer);
    }

    private static ActionExecutingContext ExecutingContext(string methodName, Type controllerType, object? args = null)
    {
        var method = controllerType.GetMethod(methodName)!;
        var descriptor = new ControllerActionDescriptor
        {
            MethodInfo = method,
            ControllerTypeInfo = controllerType.GetTypeInfo(),
        };
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/api/app/stub";
        var actionContext = new ActionContext(httpContext, new RouteData(), descriptor);
        var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (args != null)
        {
            foreach (var kv in (Dictionary<string, object?>)args)
            {
                arguments[kv.Key] = kv.Value;
            }
        }

        return new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), arguments, controllerType);
    }

    private static ActionExecutedContext ExecutedContext(ActionExecutingContext executing, object? resultValue, Exception? exception = null)
    {
        var result = resultValue switch
        {
            IActionResult actionResult => actionResult,
            _ => new ObjectResult(resultValue),
        };
        var executed = new ActionExecutedContext(executing, new List<IFilterMetadata>(), executing.Controller)
        {
            Result = result,
        };
        if (exception != null)
        {
            executed.Exception = exception;
        }

        return executed;
    }

    [Fact]
    public async Task Success_Renders_Templates_With_Args_And_Return_Value()
    {
        var (filter, writer) = Build();
        var id = Guid.NewGuid();
        var executing = ExecutingContext(nameof(StubController.Create), typeof(StubController),
            new Dictionary<string, object?> { ["id"] = id, ["name"] = "岗位A" });

        await filter.OnActionExecutionAsync(executing, () =>
            Task.FromResult(ExecutedContext(executing, new StubResult { Value = "rv" })));

        writer.Entries.Count.ShouldBe(1);
        var entry = writer.Entries[0];
        entry.Success.ShouldBeTrue();
        entry.BizId.ShouldBe(id.ToString());
        entry.Action.ShouldBe("创建了 岗位A（rv）");
        entry.RequestMethod.ShouldBe("POST");
        entry.CorrelationId.ShouldBe(TestCorrelationId);
        entry.Duration.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task Parse_Function_Resolves_Entity_Id_Into_Name()
    {
        var (filter, writer) = Build(new StubParseFunction("user", "zhangsan"));
        var executing = ExecutingContext(nameof(StubController.Lock), typeof(StubController),
            new Dictionary<string, object?> { ["id"] = Guid.NewGuid() });

        await filter.OnActionExecutionAsync(executing, () =>
            Task.FromResult(ExecutedContext(executing, null)));

        var entry = writer.Entries.ShouldHaveSingleItem();
        entry.Action.ShouldStartWith("锁定了用户 zhangsan");
    }

    [Fact]
    public async Task Action_Exception_Renders_Fail_Template_With_Error()
    {
        var (filter, writer) = Build();
        var id = Guid.NewGuid();
        var executing = ExecutingContext(nameof(StubController.Delete), typeof(StubController),
            new Dictionary<string, object?> { ["id"] = id });

        await filter.OnActionExecutionAsync(executing, () =>
            Task.FromResult(ExecutedContext(executing, null, new InvalidOperationException("业务炸了"))));

        var entry = writer.Entries.ShouldHaveSingleItem();
        entry.Success.ShouldBeFalse();
        entry.Action.ShouldStartWith($"删除 {id} 失败：");
        entry.Action.ShouldContain("业务炸了");
        entry.ErrorMessage.ShouldBe("业务炸了");
    }

    [Fact]
    public async Task Next_Throwing_Still_Records_Failure_And_Rethrows()
    {
        var (filter, writer) = Build();
        var id = Guid.NewGuid();
        var executing = ExecutingContext(nameof(StubController.Delete), typeof(StubController),
            new Dictionary<string, object?> { ["id"] = id });

        await Should.ThrowAsync<InvalidOperationException>(() =>
            filter.OnActionExecutionAsync(executing, () => throw new InvalidOperationException("管线炸了")));

        var entry = writer.Entries.ShouldHaveSingleItem();
        entry.Success.ShouldBeFalse();
        entry.ErrorMessage.ShouldBe("管线炸了");
    }

    [Fact]
    public async Task Error_Status_Code_Result_Is_Failure()
    {
        var (filter, writer) = Build();
        var executing = ExecutingContext(nameof(StubController.Conflict), typeof(StubController));

        await filter.OnActionExecutionAsync(executing, () =>
            Task.FromResult(ExecutedContext(executing, new ConflictResult())));

        var entry = writer.Entries.ShouldHaveSingleItem();
        entry.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task Class_Level_Attribute_Falls_Back_For_Unannotated_Method()
    {
        var (filter, writer) = Build();
        var executing = ExecutingContext(nameof(ClassLevelController.Ping), typeof(ClassLevelController));

        await filter.OnActionExecutionAsync(executing, () =>
            Task.FromResult(ExecutedContext(executing, null)));

        var entry = writer.Entries.ShouldHaveSingleItem();
        entry.SubType.ShouldBe("类级兜底");
        entry.Action.ShouldBe("类级注解生效");
    }

    [Fact]
    public async Task No_Attribute_Passes_Through_Without_Recording()
    {
        var (filter, writer) = Build();
        var executing = ExecutingContext(nameof(StubController.NoAttribute), typeof(StubController));
        var nextCalled = false;

        await filter.OnActionExecutionAsync(executing, () =>
        {
            nextCalled = true;
            return Task.FromResult(ExecutedContext(executing, null));
        });

        nextCalled.ShouldBeTrue();
        writer.Entries.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Writer_Failure_Does_Not_Break_The_Response()
    {
        var failing = new ThrowingWriter();
        var filter = new OperationLogActionFilter(
            failing,
            new FakeCorrelationIdProvider(),
            Array.Empty<IOperationLogParseFunction>(),
            NullLogger<OperationLogActionFilter>.Instance);
        var executing = ExecutingContext(nameof(ClassLevelController.Ping), typeof(ClassLevelController));

        // 记录动作但 writer 炸：不向上抛即通过（日志基建不传染业务）
        await filter.OnActionExecutionAsync(executing, () =>
            Task.FromResult(ExecutedContext(executing, null)));
    }

    private sealed class ThrowingWriter : IOperationLogWriter
    {
        public Task WriteAsync(OperationLogEntry entry, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("日志库炸了");
    }
}
