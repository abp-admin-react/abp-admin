using System;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 限流分区键的有界化工具。分区键会进入计数缓存键（Redis）——对无界用户输入做分区
/// 是微软限流文档点名的 DoS 反模式（"partitioning on unbounded user-controlled input
/// can exhaust memory"），Envoy Gateway 文档同款警告。IP/用户ID/租户ID 等服务端可控
/// 值天然有界无需处理；用户名/邮箱/电话等登录输入经此截断。
/// </summary>
public static class OperationRateLimitingPartitionKeys
{
    /// <summary>
    /// 对齐 ABP IdentityUserConsts.MaxUserNameLength（256）：合法用户名/邮箱不可能超过，
    /// 超出即攻击输入。截断方向安全——超长输入合并进同一个桶只会把自己锁死（碰撞只对
    /// 攻击者不利），合法用户零影响。
    /// </summary>
    public const int MaxLength = 256;

    /// <summary>截断超过 <see cref="MaxLength"/> 的分区键；其余原样返回。</summary>
    public static string? Bound(string? value)
        => string.IsNullOrEmpty(value) || value.Length <= MaxLength ? value : value[..MaxLength];
}
