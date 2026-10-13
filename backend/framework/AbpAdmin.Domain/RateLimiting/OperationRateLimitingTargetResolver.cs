using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流目标解析器（问题5 修复）：
/// MVC Filter 与 DI 拦截器原先各维护一套近乎相同的「取特性 + 解析分区参数」逻辑（约 70 行/套），
/// 任何一处修 bug 另一处必漏。本类是唯一实现，两处适配层只做各自的元数据适配。
/// </summary>
public class OperationRateLimitingTargetResolver : ITransientDependency
{
    private readonly ILogger<OperationRateLimitingTargetResolver> _logger;

    public OperationRateLimitingTargetResolver(ILogger<OperationRateLimitingTargetResolver> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// 取 [OperationRateLimiting] 特性：方法级优先于类级。
    /// 注意：MVC Filter 侧优先走 EndpointMetadata（含 Razor Pages 约定特性），本方法作为拦截器/回退路径。
    /// </summary>
    public virtual OperationRateLimitingAttribute? GetAttribute(MethodInfo? method)
    {
        if (method == null)
        {
            return null;
        }

        return method.GetCustomAttribute<OperationRateLimitingAttribute>(inherit: true)
            ?? method.DeclaringType?.GetCustomAttribute<OperationRateLimitingAttribute>(inherit: true);
    }

    /// <summary>
    /// 解析分区参数（方法无参数或无绑定实参时静默返回 null）：
    /// 优先级 1：标了 [RateLimitingParameter] 的方法参数（一个方法只能标一个，标多个抛 AbpException）；
    /// 优先级 2：实现 IHasOperationRateLimitingParameter 的实参对象；
    /// 都不满足时返回 null 并打 Warning（此时计数桶按 ClientIp 兜底分区，见 OperationRateLimitingChecker）。
    /// </summary>
    /// <param name="parameters">方法参数声明（有序）。</param>
    /// <param name="argumentsByName">参数名 → 绑定后的实参值。</param>
    /// <param name="targetName">告警/异常信息里展示的目标名（Action 或方法的显示名）。</param>
    /// <param name="policyName">策略名（告警用）。</param>
    public virtual string? ResolveParameter(
        IReadOnlyList<ParameterInfo> parameters,
        IDictionary<string, object?> argumentsByName,
        string targetName,
        string? policyName)
    {
        if (parameters.Count == 0 || argumentsByName.Count == 0)
        {
            return null;
        }

        // 优先级 1：标了 [RateLimitingParameter] 的方法参数（一个方法只能标一个）
        var markedParams = parameters
            .Where(p => p.GetCustomAttribute<RateLimitingParameterAttribute>() != null)
            .ToList();

        if (markedParams.Count > 1)
        {
            throw new AbpException(
                $"{targetName} 标了多个 [RateLimitingParameter] 特性，一个方法只能标一个。");
        }

        if (markedParams.Count == 1 && argumentsByName.TryGetValue(markedParams[0].Name!, out var value))
        {
            return value?.ToString();
        }

        // 优先级 2：参数类型实现了 IHasOperationRateLimitingParameter 接口（按声明顺序取第一个）
        foreach (var parameter in parameters)
        {
            if (argumentsByName.TryGetValue(parameter.Name!, out var argValue) &&
                argValue is IHasOperationRateLimitingParameter hasParameter)
            {
                return hasParameter.GetPartitionParameter();
            }
        }

        _logger.LogWarning(
            "操作限流特性已应用到 {TargetName}，但未能从参数解析到分区值（策略：{PolicyName}）。分区将按 ClientIp 兜底。",
            targetName,
            policyName);

        return null;
    }
}
