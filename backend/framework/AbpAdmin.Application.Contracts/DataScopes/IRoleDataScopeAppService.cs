using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.DataScopes;

public interface IRoleDataScopeAppService : IApplicationService
{
    Task<PagedResultDto<RoleDataScopeDto>> GetListAsync(PagedAndSortedResultRequestDto input);
    Task<RoleDataScopeDto> GetAsync(Guid id);
    Task<RoleDataScopeDto> GetByRoleNameAsync(string roleName);
    Task<RoleDataScopeDto> CreateAsync(CreateUpdateRoleDataScopeDto input);
    Task<RoleDataScopeDto> UpdateAsync(Guid id, CreateUpdateRoleDataScopeDto input);
    Task DeleteAsync(Guid id);
}
