using System;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Entities;
using AbpAdmin.Biz.Template.Localization;
using AbpAdmin.Biz.Template.Permissions;
using AbpAdmin.Biz.Template.Services.Dtos;
using AbpAdmin.DataScopes;
using AbpAdmin.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Localization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Validation;

namespace AbpAdmin.Biz.Template.Services;

/// <summary>
/// 数据权限示例的最小 AppService：演示业务模块消费框架数据范围基建。
/// 实体实现 IHasDataScope + 模块 DbContext 的数据范围筛选器后，这里的查询无需写任何
/// 过滤条件——GetListAsync 返回的就是当前用户数据范围内可见的行（范围由角色的
/// RoleDataScope 配置决定，配置入口在框架「身份管理 → 角色 → 数据权限」）。
/// Auto API：/api/app/biz-data-scope-demo。
/// 写入侧比框架演示夹具多一道防线（<see cref="EnsureOrganizationUnitAllowedAsync"/>）：
/// 本模块是可复制的业务样板，客户端提交的 organizationUnitId 不可信。
/// </summary>
[Authorize(BizTemplatePermissions.DataScopeDemo.Default)]
public class BizDataScopeDemoAppService : ApplicationService, IBizDataScopeDemoAppService
{
    /// <summary>排序白名单（安全审计 M-4）：Sorting 直达 Dynamic LINQ 的字符串 OrderBy，非法值应 400 而非 500。</summary>
    private static readonly string[] SortableFields =
    [
        nameof(BizDataScopeDemo.Name),
        nameof(BizDataScopeDemo.OrganizationUnitId),
        nameof(BizDataScopeDemo.CreationTime)
    ];

    private readonly IRepository<BizDataScopeDemo, Guid> _repository;
    private readonly IOrganizationUnitRepository _organizationUnitRepository;
    private readonly ICurrentDataScopeState _dataScopeState;
    private readonly IStringLocalizer<AbpAdminResource> _frameworkLocalizer;

    public BizDataScopeDemoAppService(
        IRepository<BizDataScopeDemo, Guid> repository,
        IOrganizationUnitRepository organizationUnitRepository,
        ICurrentDataScopeState dataScopeState,
        IStringLocalizer<AbpAdminResource> frameworkLocalizer)
    {
        _repository = repository;
        _organizationUnitRepository = organizationUnitRepository;
        _dataScopeState = dataScopeState;
        _frameworkLocalizer = frameworkLocalizer;
        LocalizationResource = typeof(BizTemplateResource);
    }

    public async Task<PagedResultDto<BizDataScopeDemoDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        // 白名单键在框架资源（AbpAdminResource）里，本模块资源不继承——用注入的框架本地化器
        if (!SortingWhitelist.IsValid(input.Sorting, SortableFields))
        {
            throw new AbpValidationException(
                _frameworkLocalizer["AbpAdmin:InvalidSortingFormat", input.Sorting ?? string.Empty]);
        }

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
        await EnsureOrganizationUnitAllowedAsync(input.OrganizationUnitId);

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

    /// <summary>
    /// 写入侧数据权限防线：显式指定归属组织时，该组织必须落在当前数据范围内
    /// （IsAll 例外——但校验组织确实存在）。放行越权归属等于允许把数据种进
    /// 别人的可见范围（归属伪造：SelfOnly 用户可向任意 OU 植入他人可见的数据），
    /// 或种进不存在的组织造成全员不可见。留空不在此拦——DbContext 写入侧自动填充
    /// / 算不出组织抛异常的那套语义照常兜底。
    /// SelfOnly（可见组织集合为空）的成员显式指定任何组织都会被拒：
    /// 其范围语义是「仅本人创建的行」，指定组织归属整体超出范围——与读取侧 fail-closed 同一口径。
    /// </summary>
    private async Task EnsureOrganizationUnitAllowedAsync(Guid? organizationUnitId)
    {
        if (organizationUnitId is not { } ouId)
        {
            return;
        }

        if (_dataScopeState.IsAll)
        {
            // All 范围没有可见集合可比对，退而校验组织确实存在（fail-closed：不存在即拒）
            if (await _organizationUnitRepository.FindAsync(ouId) == null)
            {
                throw new UserFriendlyException(L["BizDataScope:OrganizationUnitNotFound"]);
            }

            return;
        }

        if (!_dataScopeState.OrganizationUnitIds.Contains(ouId))
        {
            throw new UserFriendlyException(L["BizDataScope:OrganizationUnitOutOfScope"]);
        }
    }
}
