using System;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Validation;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 数据权限演示实体的最小 AppService（验收夹具）。
/// 接口级 [Authorize] 即可，不必进 access.ts。
/// </summary>
[Authorize(AbpAdminPermissions.DataScopes.Demo)]
public class DataScopeDemoAppService : ApplicationService, IDataScopeDemoAppService
{
    /// <summary>排序白名单（安全审计 M-4）：Sorting 直达 Dynamic LINQ 的字符串 OrderBy，非法值应 400 而非 500。</summary>
    private static readonly string[] SortableFields =
    [
        nameof(DataScopeDemo.Name),
        nameof(DataScopeDemo.OrganizationUnitId),
        nameof(DataScopeDemo.CreationTime)
    ];

    private readonly IRepository<DataScopeDemo, Guid> _repository;

    public DataScopeDemoAppService(IRepository<DataScopeDemo, Guid> repository)
    {
        _repository = repository;
        LocalizationResource = typeof(AbpAdminResource);
    }

    public async Task<PagedResultDto<DataScopeDemoDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        if (!SortingWhitelist.IsValid(input.Sorting, SortableFields))
        {
            throw new AbpValidationException(L["AbpAdmin:InvalidSortingFormat", input.Sorting ?? string.Empty]);
        }

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
