using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Identity;

public interface IIdentitySessionAppService : IApplicationService
{
    Task<PagedResultDto<IdentitySessionDto>> GetListAsync(GetIdentitySessionListInput input);

    Task RevokeAsync(Guid id);

    /// <summary>
    /// 吊销指定用户的全部会话（强制该用户全端下线）。
    /// </summary>
    Task RevokeAllByUserAsync(Guid userId);
}

public class GetIdentitySessionListInput : PagedAndSortedResultRequestDto
{
    public Guid? UserId { get; set; }

    public string? Device { get; set; }

    public string? ClientId { get; set; }
}

public class IdentitySessionDto : EntityDto<Guid>
{
    public string SessionId { get; set; } = default!;

    public Guid UserId { get; set; }

    public string? Device { get; set; }

    public string? DeviceInfo { get; set; }

    public string? ClientId { get; set; }

    public string? IpAddresses { get; set; }

    /// <summary>最近 IP 的归属地（离线库批量解析，内网/回环为「内网IP」，无法解析为 null）。
    /// 与审计/操作日志同源的纯展示增强——读时解析不落库，xdb 缺失时整列为空不报错。</summary>
    public string? IpRegion { get; set; }

    public DateTime SignedIn { get; set; }

    public DateTime? LastAccessed { get; set; }
}
