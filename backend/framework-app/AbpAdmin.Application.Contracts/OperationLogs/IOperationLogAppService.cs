using System.Threading.Tasks;
using AbpAdmin.Grids;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.OperationLogs;

public interface IOperationLogAppService : IApplicationService
{
    Task<PagedResultDto<OperationLogDto>> GetListAsync(GetOperationLogListInput input);
    Task<PagedResultDto<OperationLogDto>> QueryAsync(GridQueryInput input);
}
