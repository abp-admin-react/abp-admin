using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Tracing;

namespace AbpAdmin.OperationLogs;

/// <summary>
/// 语义化操作日志采集过滤器（对标 mzt-biz-log 的 AOP 切面）。
/// 自动 API 控制器的 MethodInfo 就是应用服务方法，因此读取其上的 [OperationLog] 即可覆盖
/// 全部应用服务；ABP 的 UoW/审计拦截器在服务方法返回时已提交，本过滤器在 next() 之后
/// 独立 UoW 写日志，业务回滚不会吞掉失败日志。
/// 日志链路自身的任何异常只告警、不上抛。
/// </summary>
public class OperationLogActionFilter : IAsyncActionFilter, ITransientDependency
{
    // ActionDescriptor → 注解缓存：命中注解的接口每次请求都要查找，未命中的请求只查字典
    private static readonly ConcurrentDictionary<ActionDescriptor, OperationLogAttribute?> AttributeCache = new();

    private readonly IOperationLogWriter _writer;
    private readonly ICorrelationIdProvider _correlationIdProvider;
    private readonly IReadOnlyDictionary<string, IOperationLogParseFunction> _parseFunctions;
    private readonly ILogger<OperationLogActionFilter> _logger;

    public OperationLogActionFilter(
        IOperationLogWriter writer,
        ICorrelationIdProvider correlationIdProvider,
        IEnumerable<IOperationLogParseFunction> parseFunctions,
        ILogger<OperationLogActionFilter> logger)
    {
        _writer = writer;
        _correlationIdProvider = correlationIdProvider;
        // 重名防御：DI 的 IEnumerable<T> 允许多模块贡献实现，ToDictionary 遇重名会在构造期抛异常
        // 让所有请求 500——违反日志基建 fail-open 约束。改为先到先得。
        _parseFunctions = parseFunctions
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var attribute = AttributeCache.GetOrAdd(
            context.ActionDescriptor,
            static descriptor => ResolveAttribute(descriptor));

        if (attribute == null)
        {
            await next();
            return;
        }

        // 先建立模板变量作用域：方法体内 OperationLogScope.Set 的写入对本过滤器的渲染可见
        OperationLogScope.Begin();

        var stopwatch = Stopwatch.StartNew();
        ActionExecutedContext? executed = null;
        Exception? pipelineException = null;
        try
        {
            executed = await next();
        }
        catch (Exception ex)
        {
            // next() 自身抛异常（内层 filter 的 before/after 阶段失败）：
            // 不捕获就完全不记录——配置了 Fail 模板的操作会凭空消失审计记录
            pipelineException = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();
            await RecordAsync(context, executed, pipelineException, attribute, stopwatch.Elapsed);
        }
    }

    private static OperationLogAttribute? ResolveAttribute(ActionDescriptor descriptor)
    {
        if (descriptor is not ControllerActionDescriptor controllerDescriptor)
        {
            return null;
        }

        return controllerDescriptor.MethodInfo.GetCustomAttribute<OperationLogAttribute>()
               ?? controllerDescriptor.ControllerTypeInfo.GetCustomAttribute<OperationLogAttribute>();
    }

    private async Task RecordAsync(
        ActionExecutingContext context,
        ActionExecutedContext? executed,
        Exception? pipelineException,
        OperationLogAttribute attribute,
        TimeSpan elapsed)
    {
        try
        {
            // 失败判定三来源：next() 抛出的管线异常；action 异常（未被更内层 filter 处理时）；
            // action 返回了显式错误状态码（如 ObjectResult 4xx/5xx——当前注解方法不会，但过滤器是全局基建）。
            // ExceptionHandled = true 表示异常已被更内层 filter 转成响应，语义上按其状态码判定。
            var actionException = executed is { Exception: not null, ExceptionHandled: false }
                ? executed.Exception
                : null;
            var failure = pipelineException ?? actionException;

            var statusCode = (executed?.Result as IStatusCodeActionResult)?.StatusCode;
            var success = failure == null && statusCode is null or >= 200 and < 400;

            // 拷贝一份避免框架集合类型（IDictionary<string,object>）与渲染器参数的方差不兼容
            var arguments = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var argument in context.ActionArguments)
            {
                arguments[argument.Key] = argument.Value;
            }
            var returnValue = executed?.Result switch
            {
                ObjectResult objectResult => objectResult.Value,
                _ => null,
            };

            var subType = await OperationLogTemplates.RenderAsync(attribute.SubType, arguments, _parseFunctions, returnValue)
                          ?? attribute.SubType;
            var template = success ? attribute.Success : attribute.Fail
                ?? $"{attribute.Type}-{subType} 失败：{{{{_error}}}}";

            var http = context.HttpContext.Request;
            await _writer.WriteAsync(new OperationLogEntry
            {
                Type = attribute.Type,
                SubType = subType,
                BizId = await OperationLogTemplates.RenderAsync(attribute.BizNo, arguments, _parseFunctions, returnValue),
                Action = await OperationLogTemplates.RenderAsync(
                    template,
                    arguments,
                    _parseFunctions,
                    returnValue,
                    failure?.Message,
                    attribute.Type,
                    subType),
                Success = success,
                ErrorMessage = failure?.Message,
                RequestMethod = http.Method,
                RequestUrl = http.Path.ToString(),
                ClientIpAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent = http.Headers.UserAgent.ToString(),
                CorrelationId = _correlationIdProvider.Get(),
                Duration = (int)elapsed.TotalMilliseconds,
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "记录操作日志失败：{Url}", context.HttpContext.Request.Path);
        }
    }
}
