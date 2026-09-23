using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流检查器主实现。
/// 多规则 AND 检查顺序：先对所有规则做只读检查，全部通过后才逐个递增。
/// 注意：这不是跨规则的原子事务，两个并发请求在极端时序下可能都通过只读检查然后都递增，短暂超出限额（有意取舍）。
/// </summary>
public class OperationRateLimitingChecker : IOperationRateLimitingChecker
{
    private readonly IOperationRateLimitingPolicyProvider _policyProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly ICurrentTenant _currentTenant;
    private readonly ILogger<OperationRateLimitingChecker> _logger;
    private readonly AbpAdminOperationRateLimitingOptions _options;

    public OperationRateLimitingChecker(
        IOperationRateLimitingPolicyProvider policyProvider,
        IServiceProvider serviceProvider,
        ICurrentTenant currentTenant,
        ILogger<OperationRateLimitingChecker> logger,
        IOptions<AbpAdminOperationRateLimitingOptions> options)
    {
        _policyProvider = policyProvider;
        _serviceProvider = serviceProvider;
        _currentTenant = currentTenant;
        _logger = logger;
        _options = options.Value;
    }

    public async Task CheckAsync(string policyName, OperationRateLimitingContext context)
    {
        if (!_options.IsEnabled)
        {
            return;
        }

        var policy = GetPolicyOrThrow(policyName);
        var ruleContexts = await BuildRuleContextsAsync(policy, context);

        // 第一步：对所有规则做只读检查
        var deniedResults = new List<(OperationRateLimitingRuleConfig Config, OperationRateLimitingRuleResult Result)>();
        foreach (var (config, ruleContext, rule) in ruleContexts)
        {
            var result = await rule.CheckAsync(ruleContext);
            if (!result.IsAllowed)
            {
                deniedResults.Add((config, result));
            }
        }

        // 第二步：如果有任何规则拒绝，抛异常（不递增任何计数）
        if (deniedResults.Count > 0)
        {
            var (config, result) = deniedResults.First();
            throw CreateException(policyName, config, result, context);
        }

        // 第三步：全部通过后，逐个递增
        foreach (var (config, ruleContext, rule) in ruleContexts)
        {
            await rule.IncrementAsync(ruleContext);
        }
    }

    public async Task<bool> IsAllowedAsync(string policyName, OperationRateLimitingContext context)
    {
        if (!_options.IsEnabled)
        {
            return true;
        }

        var policy = GetPolicyOrThrow(policyName);
        var ruleContexts = await BuildRuleContextsAsync(policy, context);

        foreach (var (_, ruleContext, rule) in ruleContexts)
        {
            var result = await rule.CheckAsync(ruleContext);
            if (!result.IsAllowed)
            {
                return false;
            }
        }

        return true;
    }

    public async Task<OperationRateLimitingStatus> GetStatusAsync(string policyName, OperationRateLimitingContext context)
    {
        if (!_options.IsEnabled)
        {
            return new OperationRateLimitingStatus
            {
                IsAllowed = true,
                RemainingCount = int.MaxValue,
                RetryAfter = null,
                MaxCount = int.MaxValue,
                CurrentCount = 0
            };
        }

        var policy = GetPolicyOrThrow(policyName);
        var ruleContexts = await BuildRuleContextsAsync(policy, context);

        // 聚合所有规则的状态：取最严格的（IsAllowed=false 优先，否则 RemainingCount 最小）
        OperationRateLimitingStatus? mostRestrictive = null;
        foreach (var (config, ruleContext, rule) in ruleContexts)
        {
            var status = await rule.GetStatusAsync(ruleContext);
            status.MaxCount = config.MaxCount;

            if (mostRestrictive == null)
            {
                mostRestrictive = status;
            }
            else
            {
                if (!status.IsAllowed && mostRestrictive.IsAllowed)
                {
                    mostRestrictive = status;
                }
                else if (status.IsAllowed == mostRestrictive.IsAllowed && status.RemainingCount < mostRestrictive.RemainingCount)
                {
                    mostRestrictive = status;
                }
            }
        }

        return mostRestrictive ?? new OperationRateLimitingStatus
        {
            IsAllowed = true,
            RemainingCount = int.MaxValue,
            RetryAfter = null,
            MaxCount = int.MaxValue,
            CurrentCount = 0
        };
    }

    public async Task ResetAsync(string policyName, OperationRateLimitingContext context)
    {
        var policy = GetPolicyOrThrow(policyName);
        var ruleContexts = await BuildRuleContextsAsync(policy, context);

        foreach (var (_, ruleContext, rule) in ruleContexts)
        {
            await rule.ResetAsync(ruleContext);
        }
    }

    private OperationRateLimitingPolicy GetPolicyOrThrow(string policyName)
    {
        var policy = _policyProvider.GetPolicy(policyName);
        if (policy == null)
        {
            throw new AbpException($"操作限流策略 '{policyName}' 未注册。请在 AbpAdminOperationRateLimitingOptions 中通过 AddPolicy 注册。");
        }
        return policy;
    }

    private async Task<List<(OperationRateLimitingRuleConfig Config, OperationRateLimitingRuleContext Context, IOperationRateLimitingRule Rule)>> BuildRuleContextsAsync(
        OperationRateLimitingPolicy policy,
        OperationRateLimitingContext context)
    {
        var result = new List<(OperationRateLimitingRuleConfig, OperationRateLimitingRuleContext, IOperationRateLimitingRule)>();

        foreach (var ruleConfig in policy.Rules)
        {
            var partitionKey = await ResolvePartitionKeyAsync(ruleConfig, context);
            var ruleKey = GetRuleKey(ruleConfig);
            var cacheKey = await BuildCacheKeyAsync(policy.Name, ruleKey, partitionKey, ruleConfig.MultiTenancy);

            var ruleContext = new OperationRateLimitingRuleContext
            {
                Context = context,
                RuleConfig = ruleConfig,
                CacheKey = cacheKey
            };

            var rule = (IOperationRateLimitingRule)_serviceProvider.GetRequiredService(ruleConfig.RuleType);
            result.Add((ruleConfig, ruleContext, rule));
        }

        return result;
    }

    private async Task<string?> ResolvePartitionKeyAsync(OperationRateLimitingRuleConfig ruleConfig, OperationRateLimitingContext context)
    {
        IOperationRateLimitingPartitionKeyResolver resolver;

        if (ruleConfig.PartitionType == OperationRateLimitingPartitionType.Custom)
        {
            if (ruleConfig.PartitionResolverName == null)
            {
                throw new AbpException("PartitionType 为 Custom 时 PartitionResolverName 不能为空。");
            }
            if (!_options.PartitionKeyResolvers.TryGetValue(ruleConfig.PartitionResolverName, out var resolverType))
            {
                throw new AbpException($"具名分区解析器 '{ruleConfig.PartitionResolverName}' 未注册。请在 AbpAdminOperationRateLimitingOptions 中通过 AddPartitionKeyResolver 注册。");
            }
            resolver = (IOperationRateLimitingPartitionKeyResolver)_serviceProvider.GetRequiredService(resolverType);
        }
        else
        {
            resolver = ruleConfig.PartitionType switch
            {
                OperationRateLimitingPartitionType.Parameter => _serviceProvider.GetRequiredService<PartitionKeyResolvers.ParameterOperationRateLimitingPartitionKeyResolver>(),
                OperationRateLimitingPartitionType.CurrentUser => _serviceProvider.GetRequiredService<PartitionKeyResolvers.CurrentUserOperationRateLimitingPartitionKeyResolver>(),
                OperationRateLimitingPartitionType.CurrentTenant => _serviceProvider.GetRequiredService<PartitionKeyResolvers.CurrentTenantOperationRateLimitingPartitionKeyResolver>(),
                OperationRateLimitingPartitionType.ClientIp => _serviceProvider.GetRequiredService<PartitionKeyResolvers.ClientIpOperationRateLimitingPartitionKeyResolver>(),
                OperationRateLimitingPartitionType.Email => _serviceProvider.GetRequiredService<PartitionKeyResolvers.EmailOperationRateLimitingPartitionKeyResolver>(),
                OperationRateLimitingPartitionType.PhoneNumber => _serviceProvider.GetRequiredService<PartitionKeyResolvers.PhoneNumberOperationRateLimitingPartitionKeyResolver>(),
                _ => throw new AbpException($"未知的分区类型：{ruleConfig.PartitionType}")
            };
        }

        return await resolver.ResolveAsync(context);
    }

    private static string GetRuleKey(OperationRateLimitingRuleConfig ruleConfig)
    {
        // 命名后用名字；不命名时由 Duration+MaxCount+PartitionType 派生
        return ruleConfig.Name
            ?? $"{ruleConfig.Duration.TotalSeconds}:{ruleConfig.MaxCount}:{ruleConfig.PartitionType}";
    }

    private async Task<string> BuildCacheKeyAsync(string policyName, string ruleKey, string? partitionKey, bool multiTenancy)
    {
        // 问题13 修复：分区解析失败（未标 [RateLimitingParameter]、DTO 未实现接口、参数值为 null）
        // 时回退 ClientIp 分区，避免所有用户/来源共用同一个 "null" 计数桶——攻击者可故意发
        // 无参/空参请求把该桶配额烧光，造成对正常用户的拒绝服务。IP 也拿不到（非 HTTP 上下文）
        // 才落到 "unknown"。
        var partition = partitionKey;
        if (partition == null)
        {
            partition = await _serviceProvider
                .GetRequiredService<PartitionKeyResolvers.ClientIpOperationRateLimitingPartitionKeyResolver>()
                .ResolveAsync(new OperationRateLimitingContext());
            partition ??= "unknown";
        }

        if (multiTenancy)
        {
            var tenantId = _currentTenant.Id?.ToString() ?? "host";
            return $"orl:t:{tenantId}:{policyName}:{ruleKey}:{partition}";
        }
        return $"orl:{policyName}:{ruleKey}:{partition}";
    }

    private AbpAdminOperationRateLimitingException CreateException(
        string policyName,
        OperationRateLimitingRuleConfig ruleConfig,
        OperationRateLimitingRuleResult result,
        OperationRateLimitingContext context)
    {
        var exception = new AbpAdminOperationRateLimitingException(result.ErrorCode!);

        // 填充 Data 字典（10 个约定键）
        exception.Data["PolicyName"] = policyName;
        exception.Data["MaxCount"] = ruleConfig.MaxCount;
        exception.Data["CurrentCount"] = ruleConfig.MaxCount - result.RemainingCount;
        exception.Data["RemainingCount"] = result.RemainingCount;
        exception.Data["RetryAfterSeconds"] = result.RetryAfter?.TotalSeconds;
        exception.Data["RetryAfterMinutes"] = result.RetryAfter?.TotalMinutes;
        exception.Data["RetryAfter"] = result.RetryAfter?.ToString();
        exception.Data["WindowDurationSeconds"] = ruleConfig.Duration.TotalSeconds;
        exception.Data["WindowDescription"] = result.WindowDescription;
        exception.Data["RuleDetails"] = $"{ruleConfig.PartitionType} 分区，{result.WindowDescription}";

        // 复制 ExtraProperties（注意不要放敏感信息）
        foreach (var (key, value) in context.ExtraProperties)
        {
            exception.Data[key] = value;
        }

        return exception;
    }
}
