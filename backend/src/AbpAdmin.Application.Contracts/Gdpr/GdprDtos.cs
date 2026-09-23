using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Volo.Abp.Content;

namespace AbpAdmin.Gdpr;

public interface IGdprRequestAppService : IApplicationService
{
    /// <summary>
    /// 发起个人数据导出请求。服务端会再次校验请求间隔。
    /// </summary>
    Task<GdprRequestDto> CreateAsync();

    /// <summary>
    /// 预检：当前用户是否允许发起新请求（距上次请求已超过 RequestTimeInterval）。
    /// </summary>
    Task<bool> IsNewRequestAllowedAsync();

    /// <summary>
    /// 获取当前用户的历史请求列表。
    /// </summary>
    Task<PagedResultDto<GdprRequestDto>> GetListAsync(PagedAndSortedResultRequestDto input);

    /// <summary>
    /// 第一步：已认证的请求所有者获取下载 token（60 分钟有效）。
    /// </summary>
    Task<string> GetDownloadTokenAsync(Guid requestId);

    /// <summary>
    /// 第二步：匿名下载端点，token 即凭据。由显式 Controller 以 HttpGet 暴露。
    /// </summary>
    Task<IRemoteStreamContent> DownloadAsync(Guid requestId, string token);

    /// <summary>
    /// 删除当前用户账户与个人数据。需要输入当前密码二次确认。
    /// </summary>
    Task DeleteCurrentUserAccountAsync(DeleteAccountInput input);
}

public class GdprRequestDto : EntityDto<Guid>
{
    public DateTime CreationTime { get; set; }

    public DateTime ReadyTime { get; set; }
}

public class DeleteAccountInput
{
    /// <summary>
    /// 当前登录密码，用于二次确认。
    /// </summary>
    [Required]
    [DisableAuditing] // 防止明文密码被序列化进 AbpAuditLogAction.Parameters（审计全开的项目里会永久留存）
    public string Password { get; set; } = default!;
}
