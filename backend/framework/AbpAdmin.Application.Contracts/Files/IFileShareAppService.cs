using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AbpAdmin.Files;

public interface IFileShareAppService : IApplicationService
{
    Task<FileShareLinkDto> CreateAsync(CreateFileShareLinkInput input);

    Task<ListResultDto<FileShareLinkDto>> GetListAsync(Guid fileId);

    Task DeleteAsync(Guid id);

    Task<IRemoteStreamContent> DownloadByTokenAsync(string token);
}

public class CreateFileShareLinkInput
{
    [Required]
    public Guid FileId { get; set; }

    /// <summary>过期时间。为空则默认 7 天；不可早于现在，也不可超过 <see cref="FileShareLinkConsts.MaxExpireDays"/> 天。</summary>
    public DateTime? ExpireTime { get; set; }

    public bool IsPublic { get; set; } = true;

    /// <summary>可选下载次数上限。null 不限次；若提供必须 &gt;= 1。</summary>
    [Range(1, int.MaxValue)]
    public int? MaxDownloads { get; set; }
}

public class FileShareLinkDto : EntityDto<Guid>
{
    public Guid FileId { get; set; }

    public string Token { get; set; } = string.Empty;

    public DateTime ExpireTime { get; set; }

    public bool IsPublic { get; set; }

    public int DownloadCount { get; set; }

    public int? MaxDownloads { get; set; }

    public string DownloadUrl { get; set; } = string.Empty;
}
