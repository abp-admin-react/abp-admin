using System;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 数据权限演示实体的最小 AppService（验收夹具）。
/// 接口级 [Authorize] 即可，不必进 access.ts。
/// </summary>
[Authorize(AbpAdminPermissions.DataScopes.Demo)]
public class DataScopeDemoAppService : ApplicationService, IDataScopeDemoAppService
{
    private readonly IRepository<DataScopeDemo, Guid> _repository;

    public DataScopeDemoAppService(IRepository<DataScopeDemo, Guid> repository)
    {
        _repository = repository;
    }

    public async Task<PagedResultDto<DataScopeDemoDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        var queryable = await _repository.GetQueryableAsync();

        var totalCount = await AsyncExecuter.CountAsync(queryable);

        var items = await AsyncExecuter.ToListAsync(
            queryable
                .OrderBy(input.Sorting ?? nameof(DataScopeDemo.CreationTime) + " desc")
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount)
        );

        return new PagedResultDto<DataScopeDemoDto>(
            totalCount,
            items.Select(x => ObjectMapper.Map<DataScopeDemo, DataScopeDemoDto>(x)).ToList()
        );
    }

    public async Task<DataScopeDemoDto> CreateAsync(CreateDataScopeDemoDto input)
    {
        var entity = new DataScopeDemo(
            GuidGenerator.Create(),
            input.Name,
            input.OrganizationUnitId
        );

        await _repository.InsertAsync(entity);

        return ObjectMapper.Map<DataScopeDemo, DataScopeDemoDto>(entity);
    }

    public async Task DeleteAsync(Guid id)
    {
        await _repository.DeleteAsync(id);
    }
}
