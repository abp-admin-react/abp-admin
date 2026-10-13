using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Abstractions;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.OpenIddict.Scopes;

namespace AbpAdmin.OpenIddict;

[Authorize(AbpAdminPermissions.OpenIddict.Scopes.Default)]
public class OpenIddictScopeAppService : AbpAdminAppService, IOpenIddictScopeAppService
{
    /// <summary>
    /// 内置 scope 共 5 个，不是数据库记录。已核实 OpenIddict.Abstractions 7.5.0：
    /// OpenIddictConstants.Scopes 顶层类含 Address/Email/Phone/Profile/Roles
    /// （Roles="roles" 在该版本已存在，无需手写字面量；另外还有 OpenId/OfflineAccess，
    /// 按任务规格不列入内置清单）。
    /// </summary>
    private static readonly string[] BuiltInScopeNames =
    {
        OpenIddictConstants.Scopes.Address,
        OpenIddictConstants.Scopes.Email,
        OpenIddictConstants.Scopes.Phone,
        OpenIddictConstants.Scopes.Profile,
        OpenIddictConstants.Scopes.Roles
    };

    private readonly IOpenIddictScopeRepository _scopeRepository;
    private readonly IOpenIddictScopeManager _scopeManager;

    public OpenIddictScopeAppService(
        IOpenIddictScopeRepository scopeRepository,
        IOpenIddictScopeManager scopeManager)
    {
        _scopeRepository = scopeRepository;
        _scopeManager = scopeManager;
    }

    public virtual async Task<PagedResultDto<OpenIddictScopeDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        var count = await _scopeRepository.GetCountAsync();
        var items = await _scopeRepository.GetListAsync(input.Sorting, input.SkipCount, input.MaxResultCount);
        return new PagedResultDto<OpenIddictScopeDto>(count, items.Select(Map).ToList());
    }

    /// <summary>托管 scope（数据库记录）+ 5 个内置 scope（IsBuiltIn=true，不在 OpenIddictScopes 表）。</summary>
    public virtual async Task<ListResultDto<OpenIddictScopeLookupDto>> GetAllAsync()
    {
        // 恢复按 Name 排序（代码审查 round1）：带 sorting 的 GetListAsync 重载在 SQL 侧排序，
        // scope 下拉列表顺序稳定，不再退化为物理存储顺序
        var items = await _scopeRepository.GetListAsync(
            sorting: nameof(OpenIddictScope.Name),
            skipCount: 0,
            maxResultCount: int.MaxValue);

        var result = items
            .Select(x => new OpenIddictScopeLookupDto
            {
                Name = x.Name!,
                DisplayName = x.DisplayName,
                IsBuiltIn = false
            })
            .ToList();

        foreach (var name in BuiltInScopeNames)
        {
            // 历史遗留数据里可能已有同名托管记录，避免列表出现重复项
            if (result.All(x => !string.Equals(x.Name, name, StringComparison.Ordinal)))
            {
                result.Add(new OpenIddictScopeLookupDto { Name = name, DisplayName = name, IsBuiltIn = true });
            }
        }

        return new ListResultDto<OpenIddictScopeLookupDto>(result);
    }

    [Authorize(AbpAdminPermissions.OpenIddict.Scopes.Create)]
    public virtual async Task<OpenIddictScopeDto> CreateAsync(CreateOpenIddictScopeDto input)
    {
        // 创建与内置 scope 同名的数据库记录会让行为未定义，直接拒绝
        RejectBuiltInName(input.Name);

        var descriptor = new OpenIddictScopeDescriptor
        {
            Name = input.Name,
            DisplayName = input.DisplayName,
            Description = input.Description
        };
        foreach (var resource in OpenIddictTextUtils.SplitList(input.Resources))
        {
            descriptor.Resources.Add(resource);
        }

        await _scopeManager.CreateAsync(descriptor);
        var entity = await _scopeRepository.FindByNameAsync(input.Name)
            ?? throw new EntityNotFoundException(typeof(OpenIddictScope), input.Name);
        return Map(entity);
    }

    [Authorize(AbpAdminPermissions.OpenIddict.Scopes.Update)]
    public virtual async Task<OpenIddictScopeDto> UpdateAsync(Guid id, UpdateOpenIddictScopeDto input)
    {
        var entity = await _scopeRepository.GetAsync(id);

        // 改名成内置 scope 名同样拒绝（同名保存=未改名，放行，兼容历史遗留数据）
        if (!string.Equals(entity.Name, input.Name, StringComparison.Ordinal))
        {
            RejectBuiltInName(input.Name);
        }

        // 与 application 相同：manager 泛型实参是 Model，写路径必须先 FindByIdAsync 拿 Model
        var scope = await FindScopeModelAsync(id);
        var descriptor = new OpenIddictScopeDescriptor();
        await _scopeManager.PopulateAsync(descriptor, scope);
        descriptor.Name = input.Name;
        descriptor.DisplayName = input.DisplayName;
        descriptor.Description = input.Description;
        descriptor.Resources.Clear();
        foreach (var resource in OpenIddictTextUtils.SplitList(input.Resources))
        {
            descriptor.Resources.Add(resource);
        }

        await _scopeManager.UpdateAsync(scope, descriptor);
        return Map(await _scopeRepository.GetAsync(id));
    }

    [Authorize(AbpAdminPermissions.OpenIddict.Scopes.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var scope = await FindScopeModelAsync(id);
        await _scopeManager.DeleteAsync(scope);
    }

    private async Task<object> FindScopeModelAsync(Guid id)
    {
        var scope = await _scopeManager.FindByIdAsync(id.ToString());
        if (scope is null)
        {
            throw new EntityNotFoundException(typeof(OpenIddictScope), id);
        }

        return scope;
    }

    private static void RejectBuiltInName(string name)
    {
        if (BuiltInScopeNames.Contains(name, StringComparer.Ordinal))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.OpenIddict.BuiltInScopeName)
                .WithData("name", name);
        }
    }

    private static OpenIddictScopeDto Map(OpenIddictScope item)
    {
        return new OpenIddictScopeDto
        {
            Id = item.Id,
            Name = item.Name,
            DisplayName = item.DisplayName,
            Description = item.Description,
            Resources = item.Resources
        };
    }
}
