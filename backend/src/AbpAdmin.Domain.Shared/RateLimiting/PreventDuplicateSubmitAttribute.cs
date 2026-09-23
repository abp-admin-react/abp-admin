using System;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 防重复提交：同一指纹的请求在 <see cref="IntervalSeconds"/> 秒窗口内只放行第一次，其余抛 AbpAdmin:DuplicateSubmit。
/// 仅方法级（[AttributeUsage] 限定 Method，与支持类级的 OperationRateLimiting 不同），标注在
/// 应用服务/领域服务的 virtual 方法上（ABP 动态代理基于继承）。
/// 语义（吸收 ruoyi-vue-pro @Idempotent）：成功占窗、异常释放——
/// 业务执行抛异常时默认立即删除窗口标记，客户端可马上重试（<see cref="DeleteKeyOnException"/>）。
/// 指纹作用域见 <see cref="Scope"/>。
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public class PreventDuplicateSubmitAttribute : Attribute
{
    /// <summary>拒绝窗口（秒）。默认 5。</summary>
    public int IntervalSeconds { get; }

    /// <summary>
    /// 幂等键作用域。默认 <see cref="PreventDuplicateSubmitScope.User"/>（同用户同方法同参数）。
    /// </summary>
    public PreventDuplicateSubmitScope Scope { get; set; } = PreventDuplicateSubmitScope.User;

    /// <summary>
    /// 业务键参数名。仅 <see cref="PreventDuplicateSubmitScope.Argument"/> 时允许设置
    /// （其他作用域下设置、或实参为 null，都会在拦截时抛 ArgumentException），指纹退化为
    /// 方法 + 该参数实参值，其余参数不参与。
    /// </summary>
    public string? ArgumentName { get; set; }

    /// <summary>
    /// 业务异常时是否立即删除窗口标记（默认 true，对标 yudao deleteKeyWhenException）。
    /// 失败即释放，允许客户端立刻重试；置 false 则回到 RuoYi @RepeatSubmit 的
    /// "进入即标记"语义（失败也占满窗口，适合外部不可靠重试的场景）。
    /// 释放只对本人占位生效（所有权令牌比对），释放失败仅告警、不影响原始业务异常上抛。
    /// 注意成功后从不删标记——那本质是分布式锁，应使用 IAbpDistributedLock；
    /// 另外释放只能覆盖拦截器内的异常，UOW 提交期异常（如唯一约束在 SaveChanges 时爆）
    /// 发生在拦截器之外，窗口由 TTL 兜底过期。
    /// </summary>
    public bool DeleteKeyOnException { get; set; } = true;

    public PreventDuplicateSubmitAttribute(int intervalSeconds = 5)
    {
        IntervalSeconds = intervalSeconds;
    }
}
