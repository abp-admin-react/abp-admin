using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Configuration;
using AbpAdmin.Biz.Template.Entities;
using AbpAdmin.Biz.Template.Localization;
using AbpAdmin.Biz.Template.Permissions;
using AbpAdmin.Biz.Template.Services.Dtos;
using AbpAdmin.Biz.Template.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.Biz.Template.Services;

/// <summary>
/// 样例服务：Auto API 自动暴露为 /api/app/biz-project（BizTemplateModule 的
/// ConventionalControllers 注册），授权走模块自带权限组。
/// 同时示范模块配置两层的读取姿势：
/// 第②层（部署期，IOptions）→ GetListAsync 的单页上限；第③层（运行期，SettingProvider）→ CreateAsync 的配额。
/// </summary>
[Authorize(BizTemplatePermissions.Projects.Default)]
public class BizProjectAppService : ApplicationService, IBizProjectAppService
{
    private readonly IRepository<BizProject, Guid> _bizProjectRepository;
    private readonly BizTemplateOptions _bizTemplateOptions;

    public BizProjectAppService(
        IRepository<BizProject, Guid> bizProjectRepository,
        IOptions<BizTemplateOptions> bizTemplateOptions)
    {
        _bizProjectRepository = bizProjectRepository;
        // 第②层：部署期配置（模块自带 JSON 基线 + 宿主可选覆盖），改配置需重启，快照一次即可
        _bizTemplateOptions = bizTemplateOptions.Value;
        LocalizationResource = typeof(BizTemplateResource);
    }

    public async Task<PagedResultDto<BizProjectDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        // 第②层（部署期配置）：单页上限是运维口径的开关，防深分页；管理员日常要调的参数用第③层 Settings。
        // Math.Max 先给上限本身兜底（误配 0/负数时退化为 1，而不是 Clamp 抛参数异常）。
        // 【契约】客户端 SkipCount 不随钳制调整：自分页的调用方必须保证 MaxResultCount ≤ MaxPageSize，
        // 否则钳制后翻页会跳行（服务端不报错）；MaxResultCount ≤ 0 视为取 1 行。
        var pageSize = Math.Clamp(input.MaxResultCount, 1, Math.Max(1, _bizTemplateOptions.MaxPageSize));

        // 样例固定按创建时间倒序；真实模块按需接入 Sorting 动态排序
        var query = (await _bizProjectRepository.GetQueryableAsync())
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(pageSize);

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
        // 第③层（运行期参数）：设置页可调的单用户配额，改后即时生效。
        // 这是「读第③层参数」的样板：SettingProvider.GetOrNullAsync + 非法值回落定义默认值（fail-closed，
        // 配置打错只会回到默认上限，不会悄悄关掉限制；显式 "0" 才是不限）。
        // 【尽力而为】count-then-insert 非原子：同用户并发创建存在竞窗，配额是软限制而非硬约束——
        // 复制本模式到计费/授权等强约束场景时，需换分布式锁或原子条件写入。
        var maxProjects = await GetMaxProjectsPerUserAsync();
        if (maxProjects > 0 &&
            await _bizProjectRepository.CountAsync(p => p.CreatorId == CurrentUser.Id) >= maxProjects)
        {
            throw new UserFriendlyException(L["BizProject:QuotaExceeded", maxProjects]);
        }

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

    /// <summary>
    /// 读第③层参数（BizTemplateSettings.Project.MaxProjectsPerUser）：
    /// 存储值（设置页/SettingManagement 表，按主机/租户）优先，无存储值回落到定义默认值；
    /// 非数字或负数的存储值按「配置非法」处理，回落到定义默认值（fail-closed）——
    /// 配置打错的后果是回到默认上限，而不是悄悄关掉限制；显式 "0" 仍是不限。
    /// </summary>
    private async Task<int> GetMaxProjectsPerUserAsync()
    {
        var rawValue = await SettingProvider.GetOrNullAsync(BizTemplateSettings.Project.MaxProjectsPerUser);
        return int.TryParse(rawValue, out var maxProjects) && maxProjects >= 0
            ? maxProjects
            : BizTemplateSettings.Project.DefaultMaxProjectsPerUser;
    }
}
