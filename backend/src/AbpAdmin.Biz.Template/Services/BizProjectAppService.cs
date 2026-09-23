using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Entities;
using AbpAdmin.Biz.Template.Localization;
using AbpAdmin.Biz.Template.Permissions;
using AbpAdmin.Biz.Template.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.Biz.Template.Services;

/// <summary>
/// 样例服务：Auto API 自动暴露为 /api/app/biz-project（BizTemplateModule 的
/// ConventionalControllers 注册），授权走模块自带权限组。
/// </summary>
[Authorize(BizTemplatePermissions.Projects.Default)]
public class BizProjectAppService : ApplicationService, IBizProjectAppService
{
    private readonly IRepository<BizProject, Guid> _bizProjectRepository;

    public BizProjectAppService(IRepository<BizProject, Guid> bizProjectRepository)
    {
        _bizProjectRepository = bizProjectRepository;
        LocalizationResource = typeof(BizTemplateResource);
    }

    public async Task<PagedResultDto<BizProjectDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        // 样例固定按创建时间倒序；真实模块按需接入 Sorting 动态排序
        var query = (await _bizProjectRepository.GetQueryableAsync())
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount);

        var totalCount = await _bizProjectRepository.GetCountAsync();
        var items = await AsyncExecuter.ToListAsync(query);
        var dtos = items.Select(x => ObjectMapper.Map<BizProject, BizProjectDto>(x)).ToList();

        return new PagedResultDto<BizProjectDto>(totalCount, dtos);
    }

    public async Task<BizProjectDto> GetAsync(Guid id)
    {
        var project = await _bizProjectRepository.GetAsync(id);
        return ObjectMapper.Map<BizProject, BizProjectDto>(project);
    }

    [Authorize(BizTemplatePermissions.Projects.Create)]
    public async Task<BizProjectDto> CreateAsync(CreateUpdateBizProjectDto input)
    {
        var project = new BizProject(GuidGenerator.Create(), input.Name, input.Description, input.IsActive);
        await _bizProjectRepository.InsertAsync(project, autoSave: true);
        return ObjectMapper.Map<BizProject, BizProjectDto>(project);
    }

    [Authorize(BizTemplatePermissions.Projects.Update)]
    public async Task<BizProjectDto> UpdateAsync(Guid id, CreateUpdateBizProjectDto input)
    {
        var project = await _bizProjectRepository.GetAsync(id);

        project.SetName(input.Name);
        project.SetDescription(input.Description);
        project.SetIsActive(input.IsActive);

        await _bizProjectRepository.UpdateAsync(project, autoSave: true);
        return ObjectMapper.Map<BizProject, BizProjectDto>(project);
    }

    [Authorize(BizTemplatePermissions.Projects.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        await _bizProjectRepository.DeleteAsync(id);
    }
}
