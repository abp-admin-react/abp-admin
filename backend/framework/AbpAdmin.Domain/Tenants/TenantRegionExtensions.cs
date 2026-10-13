using Volo.Abp.Data;

namespace AbpAdmin.Tenants;

/// <summary>
/// 租户地域归属（多机房部署，见 docs/dr-runbook.md §6 与 framework-contracts §7）的
/// 统一读写入口：值存租户实体 ExtraProperties[AbpAdminTenantConsts.RegionPropertyName]
/// （与 PackageId/EditionId 同机制，随 AbpTenants 表 ExtraProperties 列持久化）。
/// 注意只适用于 <see cref="Volo.Abp.TenantManagement.Tenant"/> 实体——ABP 10.6 的
/// TenantConfiguration 未实现 IHasExtraProperties，归属不进解析缓存项；方案二的
/// 运行期路由读取点本就是每租户连接串（TenantConfiguration.ConnectionStrings，解析
/// 链路已就绪），归属标记决定"哪个租户该拿哪个机房的连接串"，管理侧读实体即可。
/// 读侧永远有值：未显式标注回落 DefaultRegion，方案一期间全部租户语义上都归属主库机房。
/// </summary>
public static class TenantRegionExtensions
{
    public static string GetRegion(this IHasExtraProperties properties)
    {
        var region = properties.GetProperty<string>(AbpAdminTenantConsts.RegionPropertyName);
        return string.IsNullOrWhiteSpace(region) ? AbpAdminTenantConsts.DefaultRegion : region!;
    }

    public static void SetRegion(this IHasExtraProperties properties, string region)
    {
        properties.SetProperty(AbpAdminTenantConsts.RegionPropertyName, region);
    }
}
