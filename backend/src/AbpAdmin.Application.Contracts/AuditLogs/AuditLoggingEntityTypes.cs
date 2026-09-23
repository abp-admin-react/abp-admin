namespace AbpAdmin.AuditLogs;

/// <summary>
/// 需要开启变更历史的实体类型清单。
/// 与 AbpAuditingOptions.EntityHistorySelectors 的白名单保持同一份定义。
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
