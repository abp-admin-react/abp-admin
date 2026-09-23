using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Menus;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.TenantManagement;

namespace AbpAdmin.Tenants;

/// <summary>
/// 租户套餐应用领域规则：套餐解析（存在性 / 空菜单守卫）与 PackageId extra property 持久化。
/// 与读侧 <see cref="MenuManager.LoadTenantPackageMenuIdsAsync"/> 使用同一属性常量
/// （<see cref="AbpAdminTenantConsts.PackageIdPropertyName"/>），避免读写两侧字符串约定漂移
/// （漂移后果：套餐过滤静默失效、租户回落全量菜单）。
/// </summary>
public class TenantPackageManager : DomainService, ITransientDependency
{
    private readonly IRepository<TenantPackage, Guid> _tenantPackageRepository;

    public TenantPackageManager(IRepository<TenantPackage, Guid> tenantPackageRepository)
    {
        _tenantPackageRepository = tenantPackageRepository;
    }

    /// <summary>
    /// 把套餐应用到租户：只承载套餐校验与 extra property 写入；菜单树重置由调用方在租户上下文里
    /// 编排 <see cref="MenuManager.ResetTenantMenusAsync"/>。
    /// 套餐以 PackageId（强标识）持久化在租户 extra property 上：懒拷贝据此过滤。
    /// 返回租户被允许的 Host 模板菜单 Id 集；null = 全量（未配套餐 / 清空套餐关联）。
    /// </summary>
    public virtual async Task<HashSet<Guid>?> ApplyAsync(Tenant tenant, Guid? packageId)
    {
        if (packageId == null)
        {
            // null = 全量重置：清掉套餐关联，租户回到全量模板
            tenant.ExtraProperties.Remove(AbpAdminTenantConsts.PackageIdPropertyName);
            return null;
        }

        // 必须显式 Include 子集合：默认查询不加载聚合的 Menus
        var query = await _tenantPackageRepository.WithDetailsAsync(x => x.Menus);
        var package = await AsyncExecuter.FirstOrDefaultAsync(query.Where(x => x.Id == packageId.Value));
        if (package == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.TenantPackages.TenantPackageNotFound)
                .WithData("Id", packageId.Value);
        }

        if (package.Menus.Count == 0)
        {
            // 防误用守卫：空套餐放行会把租户菜单重置成空树（全租户不可见任何菜单）
            throw new BusinessException(AbpAdminDomainErrorCodes.TenantPackages.TenantPackageMenusEmpty)
                .WithData("Name", package.Name);
        }

        // 与 EditionAppService 写 TenantEditionPropertyName 同一惯例：ExtraProperties 索引器直写。
        // PackageId 持久化为字符串，读取方（MenuManager.LoadTenantPackageMenuIdsAsync）按字符串解析。
        tenant.ExtraProperties[AbpAdminTenantConsts.PackageIdPropertyName] = package.Id.ToString();
        return package.Menus.Select(x => x.TemplateMenuId).ToHashSet();
    }
}
