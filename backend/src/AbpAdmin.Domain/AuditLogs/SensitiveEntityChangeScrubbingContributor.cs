using System;
using System.Collections.Generic;
using Volo.Abp.Auditing;

namespace AbpAdmin.AuditLogs;

/// <summary>
/// 实体变更历史脱敏。AbpAuditingOptions.EntityHistorySelectors.AddAllEntities() 会把所有实体
/// 的标量属性变更（旧值→新值）落进 AbpEntityPropertyChanges，其中包括框架实体的
/// IdentityUser.PasswordHash / SecurityStamp 与 OpenIddict Token.Payload（原始令牌）、
/// OpenIddict Application.ClientSecret——仅有审计日志查看权限（无 Identity/OpenIddict 权限）的
/// 管理员也能从实体变更 UI 读到口令哈希（可离线爆破）与令牌明文。
/// 框架实体的属性上无法标注 [DisableAuditing]，故在落库前拦截：PostContributors 在
/// AuditingManager.BeforeSave 里、IAuditingStore.SaveAsync 之前执行，按属性名黑名单把
/// 新旧值改写为固定掩码——保留「该属性发生过变更」的事实，抹掉值本身。
/// </summary>
public class SensitiveEntityChangeScrubbingContributor : AuditLogContributor
{
    private const string RedactedMarker = "[REDACTED]";

    private static readonly HashSet<string> SensitivePropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        // Identity：口令哈希可被离线爆破；SecurityStamp 泄露等价于会话凭据泄露
        "PasswordHash", "Password", "SecurityStamp",
        // OpenIddict：Token.Payload 是原始访问/刷新令牌；ReferenceId 是其取回凭据；
        // Application.ClientSecret 是客户端密钥
        "Payload", "ReferenceId", "ClientSecret",
    };

    public override void PostContribute(AuditLogContributionContext context)
    {
        foreach (var entityChange in context.AuditInfo.EntityChanges)
        {
            foreach (var propertyChange in entityChange.PropertyChanges)
            {
                if (SensitivePropertyNames.Contains(propertyChange.PropertyName))
                {
                    propertyChange.OriginalValue = RedactedMarker;
                    propertyChange.NewValue = RedactedMarker;
                }
            }
        }
    }
}
