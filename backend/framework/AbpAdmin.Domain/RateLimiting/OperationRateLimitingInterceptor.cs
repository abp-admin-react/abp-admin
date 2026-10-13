using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DynamicProxy;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流拦截器。拦截标有 [OperationRateLimiting] 特性的方法。
/// 限制：服务必须从 DI 容器解析（new 出来的对象没有代理）、被拦截的方法必须是 virtual（ABP 动态代理基于继承）。
/// </summary>
public class OperationRateLimitingInterceptor : AbpInterceptor, ITransientDependency
{
    private readonly IOperationRateLimitingChecker _checker;
    private readonly OperationRateLimitingTargetResolver _targetResolver;

    public OperationRateLimitingInterceptor(
        IOperationRateLimitingChecker checker,
        OperationRateLimitingTargetResolver targetResolver)
    {
        _checker = checker;
        _targetResolver = targetResolver;
    }

    public override async Task InterceptAsync(IAbpMethodInvocation invocation)
    {
        var attribute = _targetResolver.GetAttribute(invocation.Method);
        if (attribute == null)
        {
            await invocation.ProceedAsync();
            return;
        }

        // 问题5 修复：参数解析收敛到 OperationRateLimitingTargetResolver（与 MVC Filter 共用同一实现）。
        // 拦截器实参是位置数组，按参数名转字典后交给解析器。
        var argumentsByName = new Dictionary<string, object?>(StringComparer.Ordinal);
        var parameters = invocation.Method.GetParameters();
        var arguments = invocation.Arguments;
        for (var i = 0; i < parameters.Length && i < arguments.Length; i++)
        {
            argumentsByName[parameters[i].Name!] = arguments[i];
        }

        var targetName = $"{invocation.Method.DeclaringType?.Name}.{invocation.Method.Name}";
        var parameter = _targetResolver.ResolveParameter(parameters, argumentsByName, targetName, attribute.PolicyName);

        var context = new OperationRateLimitingContext
        {
            Parameter = parameter
        };

        await _checker.CheckAsync(attribute.PolicyName, context);
        await invocation.ProceedAsync();
    }
}
