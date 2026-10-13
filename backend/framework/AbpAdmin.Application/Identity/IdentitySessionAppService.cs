using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

[Authorize(AbpAdminPermissions.Sessions.Default)]
public class IdentitySessionAppService : AbpAdminAppService, IIdentitySessionAppService
{
    private readonly IIdentitySessionRepository _sessionRepository;
    private readonly IdentitySessionManager _sessionManager;

    public IdentitySessionAppService(
        IIdentitySessionRepository sessionRepository,
        IdentitySessionManager sessionManager)
    {
        _sessionRepository = sessionRepository;
        _sessionManager = sessionManager;
    }

    public virtual async Task<PagedResultDto<IdentitySessionDto>> GetListAsync(GetIdentitySessionListInput input)
    {
        var count = await _sessionRepository.GetCountAsync(input.UserId, input.Device, input.ClientId);
        var items = await _sessionRepository.GetListAsync(
            input.Sorting,
            input.MaxResultCount,
            input.SkipCount,
            input.UserId,
            input.Device,
            input.ClientId);
        return new PagedResultDto<IdentitySessionDto>(
            count,
            items.Select(x => new IdentitySessionDto
            {
                Id = x.Id,
                SessionId = x.SessionId,
                UserId = x.UserId,
                Device = x.Device,
                DeviceInfo = x.DeviceInfo,
                ClientId = x.ClientId,
                IpAddresses = x.IpAddresses,
                SignedIn = x.SignedIn,
                LastAccessed = x.LastAccessed
            }).ToList());
    }

    [Authorize(AbpAdminPermissions.Sessions.Revoke)]
    public virtual async Task RevokeAsync(Guid id)
    {
        await _sessionManager.RevokeAsync(id);
    }

    /// <summary>
    /// 吊销指定用户的全部会话（强制全端下线）。
    /// ABP 动态 API 路由：POST /api/app/identity-session/revoke-all-by-user/{userId}
    /// </summary>
    [Authorize(AbpAdminPermissions.Sessions.Revoke)]
    [OperationLog("会话管理", "吊销用户全部会话", BizNo = "{{userId}}", Success = "吊销了用户 {{user(userId)}} 的全部会话")]
    public virtual async Task RevokeAllByUserAsync(Guid userId)
    {
        await _sessionManager.RevokeAllForUserAsync(userId);
    }
}
