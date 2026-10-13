using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;
using Volo.Abp.DependencyInjection;
using Volo.Abp.TenantManagement;

namespace AbpAdmin.Editions;

/// <summary>
/// 版本-租户关联的统一查询入口。租户的版本分配存在 Tenant.ExtraProperties
/// （见 <see cref="EditionConsts.TenantEditionPropertyName"/>），无法在数据库侧过滤，
/// 本类把「查询某版本下的租户」的谓词收敛为单一出处，供删除迁移（EditionAppService.DeleteAsync）、
/// 租户计数（GetTenantCountAsync）与缓存失效（EditionChangedTenantCacheInvalidator）三处共用，
/// 改属性名或比较逻辑只需改 <see cref="IsInEdition"/> 一处。
/// <para>中期方案：把版本分配从 ExtraProperties 迁移为可查询存储（影子列/关系表）后，
/// 本类内部改为 IQueryable 下推数据库，调用方无需变动。</para>
/// </summary>
public class EditionTenantManager : DomainService, ITransientDependency
{
    private readonly ITenantRepository _tenantRepository;

    public EditionTenantManager(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    /// <summary>某版本下的全部租户。</summary>
    public virtual async Task<List<Tenant>> GetTenantsInEditionAsync(Guid editionId)
    {
        var tenants = await _tenantRepository.GetListAsync();
        return tenants.Where(tenant => IsInEdition(tenant, editionId)).ToList();
    }

    /// <summary>某版本下的租户数量（删除确认弹窗展示受影响租户数用）。</summary>
    public virtual async Task<int> CountTenantsInEditionAsync(Guid editionId)
    {
        var tenants = await _tenantRepository.GetListAsync();
        return tenants.Count(tenant => IsInEdition(tenant, editionId));
    }

    /// <summary>谓词：租户 ExtraProperties 里记录的版本 Id（字符串）与目标版本一致。</summary>
    private static bool IsInEdition(Tenant tenant, Guid editionId)
    {
        return tenant.ExtraProperties.TryGetValue(EditionConsts.TenantEditionPropertyName, out var value) &&
               value?.ToString() == editionId.ToString();
    }
}
