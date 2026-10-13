using System;
using System.Collections.Generic;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流策略。一个策略由多条规则组成，规则之间是 AND 关系。
/// </summary>
public class OperationRateLimitingPolicy
{
    public string Name { get; }

    public List<OperationRateLimitingRuleConfig> Rules { get; } = new();

    public OperationRateLimitingPolicy(string name)
    {
        Name = name;
    }
}

/// <summary>
/// 单条规则的配置（由 RuleBuilder 产出，供 Rule 实例化使用）。
/// </summary>
public class OperationRateLimitingRuleConfig
{
    /// <summary>规则类型。默认固定窗口。</summary>
    public Type RuleType { get; set; } = typeof(FixedWindowRateLimitingRule);

    /// <summary>窗口时长。</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>窗口内最大次数。0 表示 Ban（永久拒绝）。</summary>
    public int MaxCount { get; set; }

    /// <summary>自定义错误码。为空时用默认（超限 / Ban）。</summary>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// 规则名。命名后 store key 用这个名字，改 maxCount/duration 不重置计数；
    /// 不命名时 key 由 Duration+MaxCount+PartitionType 派生，改任一参数即重置计数。
    /// </summary>
    public string? Name { get; set; }

    /// <summary>是否租户隔离（key 含租户段）。</summary>
    public bool MultiTenancy { get; set; }

    /// <summary>分区类型。</summary>
    public OperationRateLimitingPartitionType PartitionType { get; set; } = OperationRateLimitingPartitionType.Parameter;

    /// <summary>具名自定义解析器名（PartitionType = Custom 时使用）。</summary>
    public string? PartitionResolverName { get; set; }
}
