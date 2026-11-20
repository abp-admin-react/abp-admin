using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Grids;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

[Authorize(AbpAdminPermissions.SecurityLogs.Default)]
public class SecurityLogAppService : AbpAdminAppService, ISecurityLogAppService
{
    private readonly IIdentitySecurityLogRepository _securityLogRepository;

    public SecurityLogAppService(IIdentitySecurityLogRepository securityLogRepository)
    {
        _securityLogRepository = securityLogRepository;
    }

    public virtual async Task<PagedResultDto<SecurityLogDto>> QueryAsync(GridQueryInput input)
    {
        var query = await LazyServiceProvider.LazyGetRequiredService<IReadOnlyRepository<IdentitySecurityLog, Guid>>().GetQueryableAsync();
        var grid = new GridQueryDefinition<IdentitySecurityLog>()
            .Field("creationTime", x => x.CreationTime)
            .SearchableField("action", x => x.Action)
            .SearchableField("userName", x => x.UserName)
            .SearchableField("applicationName", x => x.ApplicationName)
            .SearchableField("identity", x => x.Identity)
            .SearchableField("clientId", x => x.ClientId)
            .SearchableField("clientIpAddress", x => x.ClientIpAddress);
        var page = await grid.ReadPageAsync(query, input, AsyncExecuter, "creationTime", true, x => x.Id, Map);
        return new(page.TotalCount, page.Items);
    }

    // [FromQuery] 必须显式标注：DTO 里的 Action 属性与路由值 action=GetList 同名，
    // 不标注时模型绑定会优先取路由值，导致列表永远按 Action="GetList" 过滤而查不到数据。
    public virtual async Task<PagedResultDto<SecurityLogDto>> GetListAsync([FromQuery] GetSecurityLogListInput input)
    {
        var count = await _securityLogRepository.GetCountAsync(
            input.StartTime,
            input.EndTime,
            input.ApplicationName,
            input.Identity,
            input.Action,
            userName: input.UserName,
            clientId: input.ClientId);
        var items = await _securityLogRepository.GetListAsync(
            input.Sorting,
            input.MaxResultCount,
            input.SkipCount,
            input.StartTime,
            input.EndTime,
            input.ApplicationName,
            input.Identity,
            input.Action,
            userName: input.UserName,
            clientId: input.ClientId);
        return new PagedResultDto<SecurityLogDto>(
            count,
            items.Select(Map).ToList());
    }

    private static SecurityLogDto Map(IdentitySecurityLog x) => new SecurityLogDto
    {
        Id = x.Id,
        CreationTime = x.CreationTime,
        ApplicationName = x.ApplicationName,
        Identity = x.Identity,
        Action = x.Action,
        UserName = x.UserName,
        ClientId = x.ClientId,
        ClientIpAddress = x.ClientIpAddress,
        BrowserInfo = x.BrowserInfo,
        CorrelationId = x.CorrelationId
    };
}

