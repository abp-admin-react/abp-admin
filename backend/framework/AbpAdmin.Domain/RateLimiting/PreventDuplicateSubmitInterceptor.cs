using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.DynamicProxy;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 防重复提交标记缓存项。租户隔离交给缓存层默认行为（不加 [IgnoreMultiTenancy]）。
/// <see cref="Token"/> 是本次占位的所有权令牌：业务超时超过窗口时长后键可能已被
/// 下一个请求重新占用，异常释放时凭令牌比对，只删自己占的窗口。
/// 旧版本（无 Token）的在途窗口比对不中，留给 TTL 自然过期（≤ IntervalSeconds）。
/// </summary>
[Serializable]
public class DuplicateSubmitCacheItem
{
    public string? Token { get; set; }
}

/// <summary>
/// 防重复提交拦截器。窗口语义对标 RuoYi @RepeatSubmit，抢占与释放机制吸收自 ruoyi-vue-pro @Idempotent：
/// 指纹 = 作用域（用户/全局/业务键）+ 方法全名 + 参数 JSON 的 SHA-256；命中即抛 AbpAdmin:DuplicateSubmit。
/// 参数 JSON 仅用于哈希（缓存键里只有十六进制摘要），敏感参数不会以明文进键。
/// 原子性：check-then-set 包在 IAbpDistributedLock 里（Redis:IsEnabled = true 时跨实例互斥，
/// false 时退化为进程内锁），同时到达的相同请求只有一个能占位；锁只覆盖检查+占位（毫秒级），
/// 不覆盖业务执行。业务异常时按 DeleteKeyOnException 释放窗口（锁内比对所有权令牌，防止业务
/// 超过窗口时长后误删后来者的窗口），释放失败仅告警、绝不吞掉或替换原始业务异常；成功则占满。
/// 已知边界：ABP UOW 提交发生在拦截器之外，提交期异常（如唯一约束在 SaveChanges 时爆）不会
/// 触发释放——窗口最长占满 IntervalSeconds，由 TTL 兜底。
/// 限制（与 OperationRateLimitingInterceptor 相同）：服务须从 DI 解析、方法须 virtual。
/// </summary>
public class PreventDuplicateSubmitInterceptor : AbpInterceptor, ITransientDependency
{
    /// <summary>抢锁等待上限。锁内只有两次缓存往返，正常毫秒级完成；超时视为基础设施故障。</summary>
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    private readonly IDistributedCache<DuplicateSubmitCacheItem> _cache;
    private readonly ICurrentUser _currentUser;
    private readonly ICurrentTenant _currentTenant;
    private readonly IAbpDistributedLock _distributedLock;
    private readonly ILogger<PreventDuplicateSubmitInterceptor> _logger;

    public PreventDuplicateSubmitInterceptor(
        IDistributedCache<DuplicateSubmitCacheItem> cache,
        ICurrentUser currentUser,
        ICurrentTenant currentTenant,
        IAbpDistributedLock distributedLock,
        ILogger<PreventDuplicateSubmitInterceptor> logger)
    {
        _cache = cache;
        _currentUser = currentUser;
        _currentTenant = currentTenant;
        _distributedLock = distributedLock;
        _logger = logger;
    }

    public override async Task InterceptAsync(IAbpMethodInvocation invocation)
    {
        var attribute = invocation.Method.GetCustomAttribute<PreventDuplicateSubmitAttribute>(inherit: true);
        if (attribute == null)
        {
            await invocation.ProceedAsync();
            return;
        }

        var key = CalculateCacheKey(
            _currentUser.Id?.ToString(),
            invocation.Method,
            invocation.Arguments,
            attribute.Scope,
            attribute.ArgumentName);

        // 抢占窗口：锁 + 检查 + 占位必须原子，否则并发首拍竞态会让两个相同请求同时通过。
        // 锁在 using 块结束即释放，业务执行（可能数秒）不持锁。
        // 锁键带租户段：缓存键由 ABP 缓存层自动加租户前缀，锁键不走缓存层归一化，
        // 不手动对齐的话跨租户同指纹会串到同一把锁上白白互斥。
        var lockKey = BuildLockKey(key);
        var token = Guid.NewGuid().ToString("N");
        await using (var handle = await _distributedLock.TryAcquireAsync(lockKey, LockTimeout))
        {
            if (handle == null)
            {
                // 锁超时是基础设施故障，抛基础设施异常，不要转成 DuplicateSubmit（与操作限流存储同款约定）
                throw new AbpException($"获取防重复提交分布式锁超时：{lockKey}");
            }

            var existing = await _cache.GetAsync(key);
            if (existing != null)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.RateLimiting.DuplicateSubmit)
                    .WithData("IntervalSeconds", attribute.IntervalSeconds);
            }

            await _cache.SetAsync(
                key,
                new DuplicateSubmitCacheItem { Token = token },
                new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(attribute.IntervalSeconds)
                });
        }

        try
        {
            await invocation.ProceedAsync();
        }
        catch
        {
            // 业务失败默认立即释放窗口，客户端可马上重试（对标 yudao @Idempotent deleteKeyWhenException）
            if (attribute.DeleteKeyOnException)
            {
                await TryReleaseWindowAsync(key, lockKey, token);
            }

            throw;
        }
    }

    /// <summary>
    /// 异常释放：锁内比对令牌后删除。锁等不到或释放途中出错都只记日志——
    /// 释放路径的任何异常绝不能吞掉或替换正在上抛的原始业务异常，
    /// 残留窗口由 TTL（≤ IntervalSeconds）自然过期兜底。
    /// </summary>
    private async Task TryReleaseWindowAsync(string key, string lockKey, string token)
    {
        try
        {
            await using var handle = await _distributedLock.TryAcquireAsync(lockKey, LockTimeout);
            if (handle == null)
            {
                return;
            }

            var item = await _cache.GetAsync(key);
            if (item?.Token == token)
            {
                await _cache.RemoveAsync(key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "防重复提交窗口释放失败，等待 TTL 自然过期：{Key}", key);
        }
    }

    private string BuildLockKey(string key)
    {
        return $"{key}:lock:{_currentTenant.Id?.ToString() ?? "host"}";
    }

    public static string CalculateCacheKey(
        string? userId,
        MethodInfo method,
        IReadOnlyList<object?> arguments,
        PreventDuplicateSubmitScope scope = PreventDuplicateSubmitScope.User,
        string? argumentName = null)
    {
        if (scope != PreventDuplicateSubmitScope.Argument && !string.IsNullOrEmpty(argumentName))
        {
            // 与"Argument 作用域缺 ArgumentName 当场抛"对称：反着配（设了名字没切作用域）同样当场失败，
            // 否则开发以为配了业务键去重，实际静默按全参数指纹跑
            throw new ArgumentException("ArgumentName 仅在 Scope=Argument 时有效，其他作用域请移除该配置。");
        }

        var fingerprint = new StringBuilder();
        if (scope == PreventDuplicateSubmitScope.User)
        {
            fingerprint.Append(userId ?? "anonymous").Append('|');
        }

        fingerprint
            .Append(method.DeclaringType?.FullName)
            .Append('.')
            .Append(method.Name);

        if (scope == PreventDuplicateSubmitScope.Argument)
        {
            var businessKey = ResolveArgument(method, arguments, argumentName);
            if (businessKey == null)
            {
                // null 业务键会让所有该方法的 null 键调用折叠进同一个窗口互相误拒，
                // 属于上游数据/配置错误，当场失败优于静默共享
                throw new ArgumentException(
                    $"PreventDuplicateSubmit ArgumentName=\"{argumentName}\" 的实参为 null，业务键不允许为空。");
            }

            fingerprint.Append('[').Append(argumentName).Append('=')
                .Append(SerializeArgument(businessKey))
                .Append(']');
        }
        else
        {
            fingerprint.Append('(')
                .Append(string.Join(";", arguments.Select(SerializeArgument)))
                .Append(')');
        }

        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fingerprint.ToString())));
        return "dupsubmit:" + hash;
    }

    /// <summary>Argument 作用域按参数名取实参；配置写错参数名属于编码错误，当场失败优于静默退化为 null。</summary>
    private static object? ResolveArgument(MethodInfo method, IReadOnlyList<object?> arguments, string? argumentName)
    {
        if (string.IsNullOrWhiteSpace(argumentName))
        {
            throw new ArgumentException("PreventDuplicateSubmit Scope=Argument 时必须设置 ArgumentName。");
        }

        var parameters = method.GetParameters();
        for (var i = 0; i < parameters.Length; i++)
        {
            if (string.Equals(parameters[i].Name, argumentName, StringComparison.Ordinal))
            {
                return i < arguments.Count ? arguments[i] : null;
            }
        }

        throw new ArgumentException(
            $"PreventDuplicateSubmit ArgumentName=\"{argumentName}\" 不是方法 {method.DeclaringType?.Name}.{method.Name} 的参数。");
    }

    /// <summary>
    /// 参数序列化。仅用于哈希输入，不要求可逆：可 JSON 序列化的走 JSON（字段级区分），
    /// 不可序列化的（如 IRemoteStreamContent）回退类型名 + ToString。
    /// </summary>
    private static string SerializeArgument(object? argument)
    {
        if (argument == null)
        {
            return "<null>";
        }

        try
        {
            return JsonSerializer.Serialize(argument, argument.GetType());
        }
        catch (Exception)
        {
            return $"{argument.GetType().FullName}:{argument}";
        }
    }
}

/// <summary>防重复提交拦截器注册器（与 OperationRateLimitingInterceptorRegistrar 同款钩子）。</summary>
public static class PreventDuplicateSubmitInterceptorRegistrar
{
    public static void RegisterIfNeeded(IOnServiceRegistredContext context)
    {
        if (ShouldIntercept(context.ImplementationType))
        {
            context.Interceptors.TryAdd<PreventDuplicateSubmitInterceptor>();
        }
    }

    private static bool ShouldIntercept(Type type)
    {
        if (DynamicProxyIgnoreTypes.Contains(type))
        {
            return false;
        }

        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.GetCustomAttribute<PreventDuplicateSubmitAttribute>(inherit: true) != null)
            {
                return true;
            }
        }

        return false;
    }
}
