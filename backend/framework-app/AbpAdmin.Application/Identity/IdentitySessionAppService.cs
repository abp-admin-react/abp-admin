using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Grids;
using AbpAdmin.IpRegions;
using AbpAdmin.OperationLogs;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

[Authorize(AbpAdminPermissions.Sessions.Default)]
public class IdentitySessionAppService : AbpAdminAppService, IIdentitySessionAppService
{
    private readonly IIdentitySessionRepository _sessionRepository;
    private readonly IdentitySessionManager _sessionManager;
    private readonly IIpLocationResolver _ipLocationResolver;

    public IdentitySessionAppService(
        IIdentitySessionRepository sessionRepository,
        IdentitySessionManager sessionManager,
        IIpLocationResolver ipLocationResolver)
    {
        _sessionRepository = sessionRepository;
        _sessionManager = sessionManager;
        _ipLocationResolver = ipLocationResolver;
    }

    public virtual async Task<PagedResultDto<IdentitySessionDto>> QueryAsync(GridQueryInput input)
    {
        var query = await LazyServiceProvider.LazyGetRequiredService<IReadOnlyRepository<IdentitySession, Guid>>().GetQueryableAsync();
        var grid = new GridQueryDefinition<IdentitySession>()
            .SearchableField("userId", x => x.UserId)
            .SearchableField("device", x => x.Device)
            .SearchableField("clientId", x => x.ClientId)
            .SearchableField("sessionId", x => x.SessionId)
            .SearchableField("ipAddresses", x => x.IpAddresses)
            .Field("signedIn", x => x.SignedIn)
            .Field("lastAccessed", x => x.LastAccessed);
        var page = await grid.ReadPageAsync(query, input, AsyncExecuter, "signedIn", true, x => x.Id, Map);
        await AttachRegionsAsync(page.Items);
        return new(page.TotalCount, page.Items);
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

        var dtos = items.Select(Map).ToList();
        await AttachRegionsAsync(dtos);
        return new PagedResultDto<IdentitySessionDto>(count, dtos);
    }

    private static IdentitySessionDto Map(IdentitySession x) => new IdentitySessionDto
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
    };

    private async Task AttachRegionsAsync(System.Collections.Generic.IReadOnlyList<IdentitySessionDto> dtos)
    {
        var lastIps = dtos
            .Select(x => LastIpOf(x.IpAddresses))
            .Where(ip => ip != null)
            .Distinct()
            .ToList();
        if (lastIps.Count > 0)
        {
            var regionMap = await _ipLocationResolver.ResolveManyAsync(lastIps!);
            foreach (var dto in dtos)
            {
                var lastIp = LastIpOf(dto.IpAddresses);
                if (lastIp != null && regionMap.TryGetValue(lastIp, out var region))
                {
                    dto.IpRegion = region;
                }
            }
        }
    }

    private static string? LastIpOf(string? ipAddresses) => ipAddresses?
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .LastOrDefault();

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
