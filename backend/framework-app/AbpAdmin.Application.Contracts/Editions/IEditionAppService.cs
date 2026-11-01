using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Editions;

public interface IEditionAppService : IApplicationService
{
    Task<PagedResultDto<EditionDto>> GetListAsync(PagedAndSortedResultRequestDto input);

    Task<ListResultDto<EditionDto>> GetLookupAsync();

    Task<EditionDto> CreateAsync(CreateEditionDto input);

    Task<EditionDto> UpdateAsync(Guid id, UpdateEditionDto input);

    /// <summary>
    /// 删除版本。input.MoveTenantsToEditionId 指定时把该版本下的租户迁移过去，
    /// 否则清空这些租户的版本分配。两种情况都会使受影响租户的版本缓存失效。
    /// </summary>
    Task DeleteAsync(Guid id, DeleteEditionInput input);

    /// <summary>
    /// 该版本下的租户数量（删除确认弹窗展示受影响租户数用）。
    /// </summary>
    Task<int> GetTenantCountAsync(Guid id);

    Task SetTenantEditionAsync(Guid tenantId, Guid? editionId);
}
