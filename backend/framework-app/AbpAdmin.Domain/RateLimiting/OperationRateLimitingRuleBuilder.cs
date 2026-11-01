using System;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 规则构建器。
/// </summary>
public class OperationRateLimitingRuleBuilder
{
    private readonly OperationRateLimitingRuleConfig _config;

    public OperationRateLimitingRuleBuilder(OperationRateLimitingRuleConfig config)
    {
        _config = config;
    }

    public OperationRateLimitingRuleBuilder WithFixedWindow(TimeSpan duration, int maxCount)
    {
        _config.RuleType = typeof(FixedWindowRateLimitingRule);
        _config.Duration = duration;
        _config.MaxCount = maxCount;
        return this;
    }

    public OperationRateLimitingRuleBuilder WithErrorCode(string errorCode)
    {
        _config.ErrorCode = errorCode;
        return this;
    }

    /// <summary>
    /// 命名规则。命名后 store key 用这个名字，改 maxCount/duration 不重置计数；
    /// 不命名时 key 由 Duration+MaxCount+PartitionType 派生，改任一参数即重置计数。
    /// </summary>
    public OperationRateLimitingRuleBuilder WithName(string name)
    {
        _config.Name = name;
        return this;
    }

    /// <summary>
    /// 租户隔离：缓存 key 含租户段，host 与租户、租户与租户之间计数独立。
    /// </summary>
    public OperationRateLimitingRuleBuilder WithMultiTenancy()
    {
        _config.MultiTenancy = true;
        return this;
    }

    /// <summary>直接用 context.Parameter 的值，原样不做任何规范化。</summary>
    public OperationRateLimitingRuleBuilder PartitionByParameter()
    {
        _config.PartitionType = OperationRateLimitingPartitionType.Parameter;
        return this;
    }

    /// <summary>CurrentUser.Id，未登录时为 "anonymous"。</summary>
    public OperationRateLimitingRuleBuilder PartitionByCurrentUser()
    {
        _config.PartitionType = OperationRateLimitingPartitionType.CurrentUser;
        return this;
    }

    /// <summary>CurrentTenant.Id，host 用字符串 "host"。</summary>
    public OperationRateLimitingRuleBuilder PartitionByCurrentTenant()
    {
        _config.PartitionType = OperationRateLimitingPartitionType.CurrentTenant;
        return this;
    }

    /// <summary>客户端 IP。</summary>
    public OperationRateLimitingRuleBuilder PartitionByClientIp()
    {
        _config.PartitionType = OperationRateLimitingPartitionType.ClientIp;
        return this;
    }

    /// <summary>优先 context.Parameter，为空回退 CurrentUser.Email；规范化 ToUpperInvariant()。</summary>
    public OperationRateLimitingRuleBuilder PartitionByEmail()
    {
        _config.PartitionType = OperationRateLimitingPartitionType.Email;
        return this;
    }

    /// <summary>优先 context.Parameter，为空回退 CurrentUser.PhoneNumber；剥离空格/短横线/点/圆括号，保留 + 与数字。</summary>
    public OperationRateLimitingRuleBuilder PartitionByPhoneNumber()
    {
        _config.PartitionType = OperationRateLimitingPartitionType.PhoneNumber;
        return this;
    }

    /// <summary>具名自定义解析器，通过 AddPartitionKeyResolver 注册。引用未注册的名字会在应用启动时抛异常。</summary>
    public OperationRateLimitingRuleBuilder PartitionBy(string resolverName)
    {
        _config.PartitionType = OperationRateLimitingPartitionType.Custom;
        _config.PartitionResolverName = resolverName;
        return this;
    }
}
