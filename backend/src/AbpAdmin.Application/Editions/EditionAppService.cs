using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.TenantManagement;

namespace AbpAdmin.Editions;

[Authorize(AbpAdminPermissions.Editions.Default)]
public class EditionAppService : AbpAdminAppService, IEditionAppService
{
    private readonly IRepository<Edition, Guid> _editionRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly EditionTenantManager _editionTenantManager;

    public EditionAppService(
        IRepository<Edition, Guid> editionRepository,
        ITenantRepository tenantRepository,
        EditionTenantManager editionTenantManager)
    {
        _editionRepository = editionRepository;
        _tenantRepository = tenantRepository;
        _editionTenantManager = editionTenantManager;
    }

    public virtual async Task<PagedResultDto<EditionDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        EnsureHostSide();
        var queryable = await _editionRepository.GetQueryableAsync();
        var query = queryable.OrderBy(x => x.DisplayName);
        var count = await AsyncExecuter.CountAsync(query);
        var items = await AsyncExecuter.ToListAsync(
            query.Skip(input.SkipCount).Take(input.MaxResultCount));
        return new PagedResultDto<EditionDto>(count, items.Select(Map).ToList());
    }

    public virtual async Task<ListResultDto<EditionDto>> GetLookupAsync()
    {
        EnsureHostSide();
        var items = await _editionRepository.GetListAsync();
        return new ListResultDto<EditionDto>(items.OrderBy(x => x.DisplayName).Select(Map).ToList());
    }

    [Authorize(AbpAdminPermissions.Editions.Create)]
    public virtual async Task<EditionDto> CreateAsync(CreateEditionDto input)
    {
        EnsureHostSide();
        var edition = new Edition(GuidGenerator.Create(), input.DisplayName);
        await _editionRepository.InsertAsync(edition);
        return Map(edition);
    }

    [Authorize(AbpAdminPermissions.Editions.Update)]
    public virtual async Task<EditionDto> UpdateAsync(Guid id, UpdateEditionDto input)
    {
        EnsureHostSide();
        var edition = await _editionRepository.GetAsync(id);
        edition.SetDisplayName(input.DisplayName);
        await _editionRepository.UpdateAsync(edition);
        return Map(edition);
    }

    [Authorize(AbpAdminPermissions.Editions.Delete)]
    public virtual async Task DeleteAsync(Guid id, DeleteEditionInput input)
    {
        EnsureHostSide();
        // T2.8 SaaS Pro 缺口第 5 项：删除版本时迁移租户
        if (input.MoveTenantsToEditionId == id)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Editions.CannotMoveTenantsToSameEdition);
        }

        if (input.MoveTenantsToEditionId.HasValue)
        {
            // 目标版本必须存在（不存在时抛 EntityNotFoundException）
            await _editionRepository.GetAsync(input.MoveTenantsToEditionId.Value);
        }

        var tenants = await _editionTenantManager.GetTenantsInEditionAsync(id);
        foreach (var tenant in tenants)
        {
            if (input.MoveTenantsToEditionId.HasValue)
            {
                tenant.ExtraProperties[EditionConsts.TenantEditionPropertyName] =
                    input.MoveTenantsToEditionId.Value.ToString();
            }
            else
            {
                // 没传替代版本 → 清空这些租户的版本分配
                tenant.ExtraProperties.Remove(EditionConsts.TenantEditionPropertyName);
            }

            // 租户更新会触发 EntityUpdatedEventData<Tenant>，
            // 由开源 TenantConfigurationCacheItemInvalidator 使该租户的版本缓存失效；
            // 版本删除本身触发 EditionChangedTenantCacheInvalidator 兜底（两者同一批租户，幂等）。
            await _tenantRepository.UpdateAsync(tenant);
        }

        await _editionRepository.DeleteAsync(id);
    }

    [Authorize(AbpAdminPermissions.Editions.Default)]
    public virtual async Task<int> GetTenantCountAsync(Guid id)
    {
        EnsureHostSide();
        return await _editionTenantManager.CountTenantsInEditionAsync(id);
    }

    [Authorize(AbpAdminPermissions.Editions.Update)]
    public virtual async Task SetTenantEditionAsync(Guid tenantId, Guid? editionId)
    {
        EnsureHostSide();
        var tenant = await _tenantRepository.GetAsync(tenantId);
        if (editionId.HasValue)
        {
            await _editionRepository.GetAsync(editionId.Value);
            tenant.ExtraProperties[EditionConsts.TenantEditionPropertyName] = editionId.Value.ToString();
        }
        else
        {
            tenant.ExtraProperties.Remove(EditionConsts.TenantEditionPropertyName);
        }

        await _tenantRepository.UpdateAsync(tenant);
    }

    private static EditionDto Map(Edition edition)
    {
        return new EditionDto
        {
            Id = edition.Id,
            DisplayName = edition.DisplayName
        };
    }
}
