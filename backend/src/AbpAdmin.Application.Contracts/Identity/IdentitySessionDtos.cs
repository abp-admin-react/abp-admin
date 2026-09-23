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

    public DateTime SignedIn { get; set; }

    public DateTime? LastAccessed { get; set; }
}
