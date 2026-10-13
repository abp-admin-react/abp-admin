using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.DataScopes;

public interface IDataScopeDemoAppService : IApplicationService
{
    Task<PagedResultDto<DataScopeDemoDto>> GetListAsync(PagedAndSortedResultRequestDto input);

    Task<DataScopeDemoDto> CreateAsync(CreateDataScopeDemoDto input);

    Task DeleteAsync(Guid id);
}
