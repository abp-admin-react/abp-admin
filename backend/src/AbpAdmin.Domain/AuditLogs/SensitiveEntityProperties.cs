using System;
using System.Collections.Generic;

namespace AbpAdmin.AuditLogs;

/// <summary>
/// 敏感实体属性的统一口径（写入侧脱敏 + 回滚侧防护共用同一份清单）。
/// AddAllEntities 审计会把 PasswordHash/SecurityStamp/Payload/ClientSecret 等的新旧值
/// 落进 AbpEntityPropertyChanges：
/// - 写入侧（<see cref="SensitiveEntityChangeScrubbingContributor"/>）把值改写为 <see cref="RedactedMarker"/>；
/// - 回滚侧（RestoreEntityChangeAsync）必须跳过这些属性——否则会把 "[REDACTED]" 字面量
///   写回口令哈希/安全戳/客户端密钥（凭据断裂），也必须跳过任何当前值等于掩码的属性
///   （防未来黑名单漏项时掩码经"字符串裸值兜底"路径回流）。
/// </summary>
public static class SensitiveEntityProperties
{
    /// <summary>落库掩码标记（与写入侧脱敏输出一致）。</summary>
    public const string RedactedMarker = "[REDACTED]";

    /// <summary>
    /// 敏感属性名黑名单（OrdinalIgnoreCase）。Identity：口令哈希可离线爆破、SecurityStamp 泄露
    /// 等价会话凭据泄露；OpenIddict：Payload 是原始令牌、ReferenceId 是取回凭据、ClientSecret 是客户端密钥。
    /// </summary>
    public static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "PasswordHash", "Password", "SecurityStamp",
        "Payload", "ReferenceId", "ClientSecret",
    };
}
