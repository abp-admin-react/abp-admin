using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EasyAbp.FileManagement.Files.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Content;

namespace AbpAdmin.Files;

/// <summary>
/// 文件缩略图（T3.1）。缩略图 blob 的命名与存取规则全部封装在后端，
/// 前端只消费本服务下发的 URL，不要自己拼规则。
/// </summary>
public interface IFileThumbnailAppService : IApplicationService
{
    /// <summary>
    /// 读取指定文件的缩略图。复用 EasyAbp 的文件读取授权（无权读文件就拿不到缩略图）；
    /// 文件不存在、无权限或缩略图未就绪都按 NotFound 处理（不暴露状态机细节）。
    /// </summary>
    Task<IRemoteStreamContent> GetAsync(Guid id);

    /// <summary>
    /// 文件列表（透传 EasyAbp GetListAsync，授权在模块内部生效）+ 本页文件的缩略图 URL 表。
    /// 只有 State == Done 的文件会出现在 ThumbnailUrls 里。
    /// </summary>
    Task<GetFileListWithThumbnailsOutput> GetListWithThumbnailsAsync(GetFileListInput input);
}

public class GetFileListWithThumbnailsOutput : PagedResultDto<FileInfoDto>
{
    /// <summary>FileId → 缩略图 URL（/api/app/file-thumbnail/{fileId}）。</summary>
    public Dictionary<Guid, string> ThumbnailUrls { get; set; } = new();

    public GetFileListWithThumbnailsOutput()
    {
    }

    public GetFileListWithThumbnailsOutput(long totalCount, IReadOnlyList<FileInfoDto> items)
        : base(totalCount, items)
    {
    }
}
