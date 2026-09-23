using System;

namespace AbpAdmin.Editions;

/// <summary>
/// 版本解析缓存选项（T2.8 SaaS Pro 缺口第 3 项）。
/// 对齐 Pro 的 EditionDynamicClaimsPrincipalContributorCacheOptions，
/// 作用于 TenantConfigurationCacheItem（租户的「有效版本」由 EditionAwareTenantStore 写入该缓存）。
/// </summary>
public class AbpAdminEditionCacheOptions
{
    /// <summary>
    /// 配置节名（appsettings.json 中的 "AbpAdmin:EditionCache"）。
    /// </summary>
    public const string SectionName = "AbpAdmin:EditionCache";

    /// <summary>
    /// 绝对过期，默认 1 小时。
    /// 注意失效规则（与 Pro 一致）：改/删租户、改/删版本会主动失效相关缓存；
    /// EditionEndDateUtc 自然到期不主动失效，最长要等一个绝对过期周期才反映出来。
    /// 这是有意取舍：不照做的唯一方式是每次请求都查库比较时间，
    /// 会把「版本解析」从一次缓存读变成一次数据库往返，高频路径代价过高。
    /// 业务上「版本过期后最多 1 小时才失效」可接受；需要立即生效时管理员改一次租户即可。
    /// </summary>
    public TimeSpan AbsoluteExpiration { get; set; } = TimeSpan.FromHours(1);
}
