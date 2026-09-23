namespace AbpAdmin.Tenants;

/// <summary>
/// 租户扩展属性常量
/// </summary>
public static class AbpAdminTenantConsts
{
    /// <summary>
    /// 激活状态属性名
    /// </summary>
    public const string ActivationStatePropertyName = "ActivationState";

    /// <summary>
    /// 激活到期时间属性名
    /// </summary>
    public const string ActivationEndDatePropertyName = "ActivationEndDate";

    /// <summary>
    /// 版本到期时间属性名（UTC）
    /// </summary>
    public const string EditionEndDateUtcPropertyName = "EditionEndDateUtc";

    /// <summary>
    /// 租户套餐 Id 属性名（字符串化的 Guid，强标识）。
    /// 写侧：TenantPackageManager.ApplyAsync；读侧：MenuManager.LoadTenantPackageMenuIdsAsync——
    /// 两侧共用同一常量，避免跨层字符串约定漂移（漂移后果：套餐过滤静默失效、回落全量菜单）。
    /// </summary>
    public const string PackageIdPropertyName = "PackageId";
}
