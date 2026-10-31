namespace AbpAdmin.AuditLogs;

/// <summary>
/// 需要开启变更历史的实体类型清单。
/// 审计历史本身经 EntityHistorySelectors.AddAllEntities() 全库开启；本清单是实体级
/// ViewChangeHistory 权限判定与回滚白名单的唯一数据源（两处消费共用同一份定义）。
/// </summary>
public static class AuditLoggingEntityTypes
{
    public static readonly string[] ChangeHistoryEnabled =
    {
        "Volo.Abp.Identity.IdentityUser",
        "Volo.Abp.Identity.IdentityRole",
        "Volo.Abp.TenantManagement.Tenant"
    };
}
