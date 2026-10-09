using System;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Entities;
using AbpAdmin.Biz.Template.Localization;
using AbpAdmin.Biz.Template.Permissions;
using AbpAdmin.Biz.Template.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.Biz.Template.Services;

/// <summary>
/// 数据权限示例的最小 AppService：演示业务模块消费框架数据范围基建。
/// 实体实现 IHasDataScope + 模块 DbContext 的数据范围筛选器后，这里的查询无需写任何
/// 过滤条件——GetListAsync 返回的就是当前用户数据范围内可见的行（范围由角色的
/// RoleDataScope 配置决定，配置入口在框架「身份管理 → 角色 → 数据权限」）。
/// Auto API：/api/app/biz-data-scope-demo。
/// </summary>
[Authorize(BizTemplatePermissions.DataScopeDemo.Default)]
public class BizDataScopeDemoAppService : ApplicationService, IBizDataScopeDemoAppService
{
    private readonly IRepository<BizDataScopeDemo, Guid> _repository;

    public BizDataScopeDemoAppService(IRepository<BizDataScopeDemo, Guid> repository)
    {
        _repository = repository;
        LocalizationResource = typeof(BizTemplateResource);
    }

    public async Task<PagedResultDto<BizDataScopeDemoDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        var queryable = await _repository.GetQueryableAsync();

        var totalCount = await AsyncExecuter.CountAsync(queryable);

        var items = await AsyncExecuter.ToListAsync(
            queryable
                .OrderBy(input.Sorting ?? nameof(BizDataScopeDemo.CreationTime) + " desc")
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount)
        );

        return new PagedResultDto<BizDataScopeDemoDto>(
            totalCount,
            items.Select(x => ObjectMapper.Map<BizDataScopeDemo, BizDataScopeDemoDto>(x)).ToList()
        );
    }

    public async Task<BizDataScopeDemoDto> CreateAsync(CreateBizDataScopeDemoDto input)
    {
        var entity = new BizDataScopeDemo(
            GuidGenerator.Create(),
            input.Name,
            input.OrganizationUnitId
        );

        await _repository.InsertAsync(entity);

        return ObjectMapper.Map<BizDataScopeDemo, BizDataScopeDemoDto>(entity);
    }

    public async Task DeleteAsync(Guid id)
    {
        await _repository.DeleteAsync(id);
    }
}
