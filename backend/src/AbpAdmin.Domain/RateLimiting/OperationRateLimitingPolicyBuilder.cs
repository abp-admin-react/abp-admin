using System;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 策略构建器。多条规则之间是 AND 关系。
/// </summary>
public class OperationRateLimitingPolicyBuilder
{
    private readonly OperationRateLimitingPolicy _policy;

    public OperationRateLimitingPolicyBuilder(OperationRateLimitingPolicy policy)
    {
        _policy = policy;
    }

    /// <summary>
    /// 便捷方法，等价于 AddRule(r => r.WithFixedWindow(duration, maxCount))。
    /// </summary>
    public OperationRateLimitingPolicyBuilder WithFixedWindow(TimeSpan duration, int maxCount)
        => AddRule(r => r.WithFixedWindow(duration, maxCount));

    /// <summary>
    /// 添加一条规则（与已有规则是 AND 关系）。
    /// </summary>
    public OperationRateLimitingPolicyBuilder AddRule(Action<OperationRateLimitingRuleBuilder> builder)
    {
        var config = new OperationRateLimitingRuleConfig();
        builder(new OperationRateLimitingRuleBuilder(config));
        _policy.Rules.Add(config);
        return this;
    }

    /// <summary>
    /// 添加一条自定义算法规则。
    /// </summary>
    public OperationRateLimitingPolicyBuilder AddRule<TRule>() where TRule : IOperationRateLimitingRule
    {
        _policy.Rules.Add(new OperationRateLimitingRuleConfig { RuleType = typeof(TRule) });
        return this;
    }

    public OperationRateLimitingPolicyBuilder ClearRules()
    {
        _policy.Rules.Clear();
        return this;
    }
}
