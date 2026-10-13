using System;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Services.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Biz.Template.Services;

public interface IBizProjectAppService : IApplicationService
{
    Task<PagedResultDto<BizProjectDto>> GetListAsync(PagedAndSortedResultRequestDto input);

    Task<BizProjectDto> GetAsync(Guid id);

    Task<BizProjectDto> CreateAsync(CreateUpdateBizProjectDto input);

    Task<BizProjectDto> UpdateAsync(Guid id, CreateUpdateBizProjectDto input);

    Task DeleteAsync(Guid id);
}
