using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Tenants;

public interface ITenantPackageAppService : IApplicationService
{
    Task<PagedResultDto<TenantPackageDto>> GetListAsync(TenantPackageListInput input);

    Task<TenantPackageDto> GetAsync(Guid id);

    Task<TenantPackageDto> CreateAsync(TenantPackageCreateDto input);

    Task<TenantPackageDto> UpdateAsync(Guid id, TenantPackageUpdateDto input);

    Task DeleteAsync(Guid id);

    /// <summary>套餐的菜单勾选：Host 模板树 + 已勾选的模板菜单 Id 集。</summary>
    Task<TenantPackageMenuSelectionDto> GetMenuSelectionAsync(Guid id);

    /// <summary>全量替换套餐勾选的模板菜单。</summary>
    Task UpdateMenuSelectionAsync(Guid id, UpdateTenantPackageMenusDto input);
}
