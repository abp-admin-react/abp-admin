using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.OperationLogs;

public interface IOperationLogAppService : IApplicationService
{
    Task<PagedResultDto<OperationLogDto>> GetListAsync(GetOperationLogListInput input);
}
