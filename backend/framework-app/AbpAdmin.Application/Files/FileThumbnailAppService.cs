using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Grids;
using AbpAdmin.Imaging;
using EasyAbp.FileManagement.Files.Dtos;
using EasyAbp.FileManagement.Files;
using EasyAbp.FileManagement.Permissions;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.Files;

/// <summary>
/// 文件缩略图读取与「文件列表 + 缩略图 URL」。
/// 授权复用 EasyAbp 模块自身（已核实模块的 GetAsync/GetListAsync 内部都走
/// AuthorizationService.CheckAsync 与 FileOperationAuthorizationHandler），
/// 不在本服务重新实现权限判断。
/// </summary>
[Authorize]
public class FileThumbnailAppService : AbpAdminAppService, IFileThumbnailAppService
{
    private readonly IFileAppService _fileAppService;
    private readonly IRepository<FileThumbnail, Guid> _thumbnailRepository;
    private readonly IBlobContainer<FileThumbnailContainer> _thumbnailContainer;

    public FileThumbnailAppService(
        IFileAppService fileAppService,
        IRepository<FileThumbnail, Guid> thumbnailRepository,
        IBlobContainer<FileThumbnailContainer> thumbnailContainer)
    {
        _fileAppService = fileAppService;
        _thumbnailRepository = thumbnailRepository;
        _thumbnailContainer = thumbnailContainer;
    }

    public virtual async Task<GetFileListWithThumbnailsOutput> QueryAsync(FileGridInput input)
    {
        // 文件模块按容器、目录和所有者授权；查询范围须与授权资源完全一致。
        await AuthorizationService.CheckAsync(
            new FileGetListOperationInfoModel(input.ParentId, input.FileContainerName, input.OwnerUserId),
            new OperationAuthorizationRequirement { Name = FileManagementPermissions.File.Default });
        var files = await LazyServiceProvider.LazyGetRequiredService<IFileRepository>().GetQueryableAsync();
        var rows = files.Where(x => x.ParentId == input.ParentId && x.OwnerUserId == input.OwnerUserId
            && x.FileContainerName == input.FileContainerName);
        var grid = new GridQueryDefinition<EasyAbp.FileManagement.Files.File>()
            .SearchableField("fileName", x => x.FileName)
            .SearchableField("mimeType", x => x.MimeType)
            .Field("fileType", x => (int)x.FileType)
            .Field("byteSize", x => x.ByteSize)
            .Field("subFilesQuantity", x => x.SubFilesQuantity)
            .Field("creationTime", x => x.CreationTime);
        var page = await grid.ReadPageAsync(rows, input.Grid, AsyncExecuter, "fileName", false,
            x => x.Id, x => ObjectMapper.Map<EasyAbp.FileManagement.Files.File, FileInfoDto>(x));
        return await WithThumbnailsAsync(new(page.TotalCount, page.Items));
    }

    public virtual async Task<IRemoteStreamContent> GetAsync(Guid id)
    {
        // 模块授权：无权读文件信息时这里直接抛 403/404
        var file = await _fileAppService.GetAsync(id);

        var thumbnail = await _thumbnailRepository.FindAsync(x => x.FileId == id);
        if (thumbnail?.State != FileThumbnailStateEnum.Done || thumbnail.BlobName.IsNullOrWhiteSpace())
        {
            throw new EntityNotFoundException(typeof(FileThumbnail), id);
        }

        // 直接把 blob 流交给 RemoteStreamContent（与 FileShareAppService.DownloadByTokenAsync 的流式范式一致），
        // 不整包读成 byte[] 再包 MemoryStream——多一次拷贝且照抄到大文件路径会出事
        var extension = Path.GetExtension(thumbnail.BlobName);
        var stream = await _thumbnailContainer.GetAsync(thumbnail.BlobName);

        return new RemoteStreamContent(
            stream,
            fileName: file.FileName,
            contentType: ImageMimeTypes.GetMimeType(extension) ?? "application/octet-stream");
    }

    public virtual async Task<GetFileListWithThumbnailsOutput> GetListWithThumbnailsAsync(GetFileListInput input)
    {
        // 透传模块列表（授权在模块内部生效），再补本页 Done 状态的缩略图 URL
        var result = await _fileAppService.GetListAsync(input);
        return await WithThumbnailsAsync(result);
    }

    private async Task<GetFileListWithThumbnailsOutput> WithThumbnailsAsync(PagedResultDto<FileInfoDto> result)
    {
        var output = new GetFileListWithThumbnailsOutput(result.TotalCount, result.Items);

        var fileIds = result.Items.Select(x => x.Id).ToArray();
        if (fileIds.Length == 0)
        {
            return output;
        }

        var thumbnails = await _thumbnailRepository.GetListAsync(
            x => x.State == FileThumbnailStateEnum.Done && fileIds.Contains(x.FileId));

        foreach (var thumbnail in thumbnails)
        {
            output.ThumbnailUrls[thumbnail.FileId] = $"/api/app/file-thumbnail/{thumbnail.FileId}";
        }

        return output;
    }
}
