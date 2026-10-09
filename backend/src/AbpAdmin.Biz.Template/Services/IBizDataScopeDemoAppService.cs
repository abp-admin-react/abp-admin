using System;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Services.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Biz.Template.Services;

public interface IBizDataScopeDemoAppService : IApplicationService
{
    Task<PagedResultDto<BizDataScopeDemoDto>> GetListAsync(PagedAndSortedResultRequestDto input);

    Task<BizDataScopeDemoDto> CreateAsync(CreateBizDataScopeDemoDto input);

    Task DeleteAsync(Guid id);
}
