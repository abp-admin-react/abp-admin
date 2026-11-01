using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

[Authorize(AbpAdminPermissions.ClaimTypes.Default)]
public class ClaimTypeAppService : AbpAdminAppService, IClaimTypeAppService
{
    private readonly IdentityClaimTypeManager _claimTypeManager;
    private readonly IIdentityClaimTypeRepository _claimTypeRepository;

    public ClaimTypeAppService(
        IdentityClaimTypeManager claimTypeManager,
        IIdentityClaimTypeRepository claimTypeRepository)
    {
        _claimTypeManager = claimTypeManager;
        _claimTypeRepository = claimTypeRepository;
    }

    public virtual async Task<PagedResultDto<ClaimTypeDto>> GetListAsync(PagedAndSortedResultRequestDto input)
    {
        var count = await _claimTypeRepository.GetCountAsync();
        var items = await _claimTypeRepository.GetListAsync(
            input.Sorting,
            input.MaxResultCount,
            input.SkipCount,
            filter: null);
        return new PagedResultDto<ClaimTypeDto>(count, items.Select(Map).ToList());
    }

    public virtual async Task<List<ClaimTypeDto>> GetLookupAsync()
    {
        var items = await _claimTypeRepository.GetListAsync();
        return items.Select(Map).ToList();
    }

    [Authorize(AbpAdminPermissions.ClaimTypes.Create)]
    public virtual async Task<ClaimTypeDto> CreateAsync(CreateClaimTypeDto input)
    {
        var claimType = new IdentityClaimType(
            GuidGenerator.Create(),
            input.Name,
            input.Required,
            false,
            input.Regex,
            input.RegexDescription,
            input.Description,
            input.ValueType);
        await _claimTypeManager.CreateAsync(claimType);
        return Map(claimType);
    }

    [Authorize(AbpAdminPermissions.ClaimTypes.Update)]
    public virtual async Task<ClaimTypeDto> UpdateAsync(Guid id, UpdateClaimTypeDto input)
    {
        var claimType = await _claimTypeRepository.GetAsync(id);
        if (claimType.IsStatic)
        {
            throw new UserFriendlyException(L["ClaimTypeIsStatic"]);
        }

        claimType.Required = input.Required;
        claimType.Regex = input.Regex;
        claimType.RegexDescription = input.RegexDescription;
        claimType.Description = input.Description;
        claimType.ValueType = input.ValueType;
        await _claimTypeManager.UpdateAsync(claimType);
        return Map(claimType);
    }

    [Authorize(AbpAdminPermissions.ClaimTypes.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var claimType = await _claimTypeRepository.GetAsync(id);
        if (claimType.IsStatic)
        {
            throw new UserFriendlyException(L["ClaimTypeIsStatic"]);
        }

        await _claimTypeRepository.DeleteAsync(id);
    }

    private static ClaimTypeDto Map(IdentityClaimType item)
    {
        return new ClaimTypeDto
        {
            Id = item.Id,
            Name = item.Name,
            Required = item.Required,
            IsStatic = item.IsStatic,
            Regex = item.Regex,
            RegexDescription = item.RegexDescription,
            Description = item.Description,
            ValueType = item.ValueType
        };
    }
}
// IdentityClaimAppService（用户/角色声明管理）已拆分到独立文件 IdentityClaimAppService.cs（重构报告问题 26）
