using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace AbpAdmin.Account;

[Authorize]
public class IdentityUserDelegationAppService : AbpAdminAppService, IIdentityUserDelegationAppService
{
    private readonly IdentityUserDelegationManager _delegationManager;
    private readonly IdentityUserManager _userManager;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IAccountProAppService _accountProAppService;

    public IdentityUserDelegationAppService(
        IdentityUserDelegationManager delegationManager,
        IdentityUserManager userManager,
        IIdentityUserRepository userRepository,
        IAccountProAppService accountProAppService)
    {
        _delegationManager = delegationManager;
        _userManager = userManager;
        _userRepository = userRepository;
        _accountProAppService = accountProAppService;
    }

    public virtual async Task<List<IdentityUserDelegationDto>> GetDelegatedToOthersAsync()
    {
        var list = await _delegationManager.GetListAsync(CurrentUser.GetId(), targetUserId: null);
        var dtos = list.Select(Map).ToList();
        await FillUserNamesAsync(dtos);
        return dtos;
    }

    public virtual async Task<List<IdentityUserDelegationDto>> GetDelegatedToMeAsync()
    {
        var list = await _delegationManager.GetActiveDelegationsAsync(CurrentUser.GetId());
        var dtos = list.Select(Map).ToList();
        await FillUserNamesAsync(dtos);
        return dtos;
    }

    public virtual async Task<IdentityUserDelegationDto> DelegateAsync(CreateUserDelegationInput input)
    {
        if (input.TargetUserId == CurrentUser.GetId())
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.CannotDelegateToSelf);
        }

        if (input.EndTime <= input.StartTime)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidDelegationPeriod);
        }

        var target = await _userManager.FindByIdAsync(input.TargetUserId.ToString());
        if (target == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.ImpersonationTargetUserNotFound);
        }

        await _delegationManager.DelegateNewUserAsync(
            CurrentUser.GetId(),
            input.TargetUserId,
            input.StartTime,
            input.EndTime);

        if (CurrentUnitOfWork != null)
        {
            await CurrentUnitOfWork.SaveChangesAsync();
        }

        var list = await _delegationManager.GetListAsync(CurrentUser.GetId(), input.TargetUserId);
        var delegation = list
            .Where(x => x.TargetUserId == input.TargetUserId)
            .OrderByDescending(x => x.StartTime)
            .FirstOrDefault()
            ?? throw new BusinessException(AbpAdminDomainErrorCodes.Account.DelegationNotFound);

        var dto = Map(delegation);
        await FillUserNamesAsync(new List<IdentityUserDelegationDto> { dto });
        return dto;
    }

    public virtual async Task DeleteAsync(Guid id)
    {
        var list = await _delegationManager.GetListAsync(CurrentUser.GetId(), targetUserId: null);
        if (list.All(x => x.Id != id))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.DelegationNotFound);
        }

        await _delegationManager.DeleteDelegationAsync(id, CurrentUser.GetId());
    }

    public virtual Task<ImpersonationResultDto> StartAsync(Guid id)
    {
        return _accountProAppService.StartDelegationAsync(id);
    }

    private IdentityUserDelegationDto Map(IdentityUserDelegation entity)
    {
        var now = Clock.Now;
        return new IdentityUserDelegationDto
        {
            Id = entity.Id,
            SourceUserId = entity.SourceUserId,
            TargetUserId = entity.TargetUserId,
            StartTime = entity.StartTime,
            EndTime = entity.EndTime,
            IsActive = entity.StartTime <= now && now <= entity.EndTime
        };
    }

    /// <summary>
    /// 补用户名：仓储批量接口一次查询取回全部相关用户（替代去重后逐个 FindByIdAsync 的 N+1）。
    /// 用户已删时用户名为空，不把查找失败抬成 404（列表仍要能看）。
    /// </summary>
    private async Task FillUserNamesAsync(List<IdentityUserDelegationDto> list)
    {
        var ids = list
            .SelectMany(item => new[] { item.SourceUserId, item.TargetUserId })
            .Distinct()
            .ToList();

        var users = await _userRepository.GetListByIdsAsync(ids);
        var names = users.ToDictionary(u => u.Id, u => u.UserName);

        foreach (var item in list)
        {
            item.SourceUserName = names.GetValueOrDefault(item.SourceUserId);
            item.TargetUserName = names.GetValueOrDefault(item.TargetUserId);
        }
    }
}
