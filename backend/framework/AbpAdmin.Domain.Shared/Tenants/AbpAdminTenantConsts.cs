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

    /// <summary>
    /// 地域归属属性名（多机房部署的租户归属标记，见 docs/dr-runbook.md §6）：
    /// 方案一（单主多备）只标记不路由，方案二（按租户分区多活）据此 + 每租户连接串做归属路由。
    /// 值为机房代号（约定 "A"=主库机房、"B"=备机房，可扩展）；读写入口收敛在 TenantRegionExtensions。
    /// </summary>
    public const string RegionPropertyName = "Region";

    /// <summary>
    /// 未显式标注地域的租户读出的默认归属（主库所在机房）。
    /// 读侧回落而非迁移存量数据：方案二上线前所有租户都写在主库机房，语义等价。
    /// </summary>
    public const string DefaultRegion = "A";
}
