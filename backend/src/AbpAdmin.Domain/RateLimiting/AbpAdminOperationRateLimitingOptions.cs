using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流全局配置。
/// </summary>
public class AbpAdminOperationRateLimitingOptions
{
    /// <summary>全局开关。Development 环境自动 false。</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>分布式锁超时时间。超时抛 AbpException（500），不得转 429。</summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    private readonly Dictionary<string, OperationRateLimitingPolicy> _policies = new();

    /// <summary>已注册的策略（只读）。</summary>
    public IReadOnlyDictionary<string, OperationRateLimitingPolicy> Policies => _policies.ToImmutableDictionary();

    private readonly Dictionary<string, Type> _partitionKeyResolvers = new();

    /// <summary>已注册的具名分区解析器（只读）。</summary>
    public IReadOnlyDictionary<string, Type> PartitionKeyResolvers => _partitionKeyResolvers.ToImmutableDictionary();

    /// <summary>
    /// 添加策略。同名策略会覆盖。
    /// </summary>
    public AbpAdminOperationRateLimitingOptions AddPolicy(string name, Action<OperationRateLimitingPolicyBuilder> builder)
    {
        var policy = new OperationRateLimitingPolicy(name);
        builder(new OperationRateLimitingPolicyBuilder(policy));
        _policies[name] = policy;
        return this;
    }

    /// <summary>
    /// 配置已有策略（在已有策略上追加/修改规则）。策略不存在时抛异常。
    /// </summary>
    public AbpAdminOperationRateLimitingOptions ConfigurePolicy(string name, Action<OperationRateLimitingPolicyBuilder> builder)
    {
        if (!_policies.TryGetValue(name, out var policy))
        {
            throw new InvalidOperationException($"Operation rate limiting policy '{name}' is not registered. Use AddPolicy first.");
        }
        builder(new OperationRateLimitingPolicyBuilder(policy));
        return this;
    }

    /// <summary>
    /// 移除策略。
    /// </summary>
    public AbpAdminOperationRateLimitingOptions RemovePolicy(string name)
    {
        _policies.Remove(name);
        return this;
    }

    /// <summary>
    /// 注册具名分区解析器。同名会覆盖。
    /// </summary>
    public AbpAdminOperationRateLimitingOptions AddPartitionKeyResolver(string name, Type resolverType)
    {
        if (!typeof(IOperationRateLimitingPartitionKeyResolver).IsAssignableFrom(resolverType))
        {
            throw new ArgumentException($"Type '{resolverType.FullName}' does not implement IOperationRateLimitingPartitionKeyResolver.", nameof(resolverType));
        }
        _partitionKeyResolvers[name] = resolverType;
        return this;
    }

    /// <summary>
    /// 替换具名分区解析器（等价于 AddPartitionKeyResolver，语义上强调覆盖）。
    /// </summary>
    public AbpAdminOperationRateLimitingOptions ReplacePartitionKeyResolver(string name, Type resolverType)
        => AddPartitionKeyResolver(name, resolverType);
}
