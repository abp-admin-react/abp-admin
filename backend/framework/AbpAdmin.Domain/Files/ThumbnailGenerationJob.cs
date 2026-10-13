using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Imaging;
using EasyAbp.FileManagement.Options.Containers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Imaging;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Threading;
using Volo.Abp.Uow;
using File = EasyAbp.FileManagement.Files.File;
using FileType = EasyAbp.FileManagement.Files.FileType;

namespace AbpAdmin.Files;

/// <summary>
/// 异步缩略图生成作业（T3.1）。上传请求只负责入队，CPU 密集的缩放/压缩在这里做。
///
/// 行为约定：
/// - 幂等：State == Done 直接返回（ABP 后台作业可能重试）。
/// - State 不是 Done 的处理结果按 Unsupported / Failed 落库后返回，不抛异常——
///   抛异常会让 ABP 后台作业进入重试循环，而格式不支持是重试不好的。
/// - 意外异常（BLOB provider 故障等）不吞，留给 ABP 后台作业的重试机制。
/// - 缩略图 blob 名后缀取「处理结果的实际格式」：ABP SkiaSharp contributor 不转码，
///   输出保持源格式（已对 10.6.0 源码核实，见 FileThumbnail.BlobName 注释）。
/// </summary>
public class ThumbnailGenerationJob : AsyncBackgroundJob<ThumbnailGenerationJobArgs>, ITransientDependency
{
    private readonly IRepository<FileThumbnail, Guid> _thumbnailRepository;
    private readonly EasyAbp.FileManagement.Files.IFileRepository _fileRepository;
    private readonly IFileContainerConfigurationProvider _fileContainerConfigurationProvider;
    private readonly IBlobContainerFactory _blobContainerFactory;
    private readonly IBlobContainer<FileThumbnailContainer> _thumbnailContainer;
    private readonly IImageResizer _imageResizer;
    private readonly IImageCompressor _imageCompressor;
    private readonly IImageProcessingThrottle _throttle;
    private readonly AbpAdminImagingOptions _options;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly IGuidGenerator _guidGenerator;
    private readonly ICancellationTokenProvider _cancellationTokenProvider;
    private readonly ILogger<ThumbnailGenerationJob> _logger;

    protected CancellationToken CancellationToken => _cancellationTokenProvider.Token;

    public ThumbnailGenerationJob(
        IRepository<FileThumbnail, Guid> thumbnailRepository,
        EasyAbp.FileManagement.Files.IFileRepository fileRepository,
        IFileContainerConfigurationProvider fileContainerConfigurationProvider,
        IBlobContainerFactory blobContainerFactory,
        IBlobContainer<FileThumbnailContainer> thumbnailContainer,
        IImageResizer imageResizer,
        IImageCompressor imageCompressor,
        IImageProcessingThrottle throttle,
        IOptions<AbpAdminImagingOptions> options,
        IUnitOfWorkManager unitOfWorkManager,
        ICurrentTenant currentTenant,
        IGuidGenerator guidGenerator,
        ICancellationTokenProvider cancellationTokenProvider,
        ILogger<ThumbnailGenerationJob> logger)
    {
        _thumbnailRepository = thumbnailRepository;
        _fileRepository = fileRepository;
        _fileContainerConfigurationProvider = fileContainerConfigurationProvider;
        _blobContainerFactory = blobContainerFactory;
        _thumbnailContainer = thumbnailContainer;
        _imageResizer = imageResizer;
        _imageCompressor = imageCompressor;
        _throttle = throttle;
        _options = options.Value;
        _unitOfWorkManager = unitOfWorkManager;
        _currentTenant = currentTenant;
        _guidGenerator = guidGenerator;
        _cancellationTokenProvider = cancellationTokenProvider;
        _logger = logger;
    }

    public override async Task ExecuteAsync(ThumbnailGenerationJobArgs args)
    {
        // 租户上下文必须显式切换（00-overview 6.5），且在开 UoW 之前切（6.5 嵌套顺序）
        using (_currentTenant.Change(args.TenantId))
        {
            // UoW #1：取（或补）映射行 + 读源文件实体
            var (thumbnail, file) = await EnsureThumbnailRowAsync(args);

            // 幂等：已 Done 直接返回
            if (thumbnail.State == FileThumbnailStateEnum.Done)
            {
                return;
            }

            // 源文件已删除：映射行与残留缩略图 blob 一并清掉（缩略图可随时重建，无保留价值）
            if (file is null)
            {
                await DeleteThumbnailAsync(thumbnail.Id, thumbnail.BlobName);
                return;
            }

            // 校验 + 取源流 + 解压炸弹前置检查；不可处理时已落 Unsupported/Failed 并返回 null
            var source = await TryOpenValidSourceAsync(file, thumbnail.Id);
            if (source is null)
            {
                return;
            }

            await using (source)
            {
                // 闸门内缩放 + 压缩；不可处理时已落 Unsupported 并返回 null
                var processed = await ProcessAsync(source, file, thumbnail.Id);
                if (processed is null)
                {
                    return;
                }

                await using (processed)
                {
                    // 读处理结果的头 + 存 blob + Done 落库；头不可读时已落 Failed 并返回 null
                    var blobName = await PersistThumbnailAsync(thumbnail.Id, processed, args.FileId);
                    if (blobName is not null)
                    {
                        // 用 PersistThumbnailAsync 实际写入的 blob 名（thumbnail 实例是 UoW #1 的旧实例，
                        // 其 BlobName 从未被赋值，恒为 null）
                        _logger.LogDebug("Generated thumbnail {BlobName} for file {FileId}", blobName, args.FileId);
                    }
                }
            }
        }
    }

    /// <summary>UoW #1：取（或补）映射行 + 读源文件实体。快速只读操作，与后面的 CPU 密集段分开。</summary>
    private async Task<(FileThumbnail Thumbnail, File? File)> EnsureThumbnailRowAsync(ThumbnailGenerationJobArgs args)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var thumbnail = await _thumbnailRepository.FindAsync(x => x.FileId == args.FileId, cancellationToken: CancellationToken);
        if (thumbnail is null)
        {
            thumbnail = new FileThumbnail(_guidGenerator.Create(), args.TenantId, args.FileId);
            await _thumbnailRepository.InsertAsync(thumbnail, cancellationToken: CancellationToken);
        }

        var file = await _fileRepository.FindAsync(args.FileId, cancellationToken: CancellationToken);
        await uow.CompleteAsync();
        return (thumbnail, file);
    }

    /// <summary>
    /// 文件校验（常规文件 + 扩展名白名单）+ 取源流 + 解码前像素上限检查（防解压炸弹）。
    /// 不可处理时把映射行落为 Unsupported/Failed 并返回 null；意外异常（blob provider 故障等）
    /// 向上抛，留给 ABP 后台作业的重试机制。
    /// </summary>
    private async Task<Stream?> TryOpenValidSourceAsync(File file, Guid thumbnailId)
    {
        if (file.FileType != FileType.RegularFile)
        {
            await UpdateStateAsync(thumbnailId, row => row.MarkUnsupported("Not a regular file"));
            return null;
        }

        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
        if (!_options.ThumbnailSourceExtensions.Contains(extension))
        {
            await UpdateStateAsync(thumbnailId, row => row.MarkUnsupported($"Extension '{extension}' is not in ThumbnailSourceExtensions"));
            return null;
        }

        // 从源容器取流（与 FileBlobManager 同款路径；IFileBlobManager 只有 byte[] 版，流式自己组）
        var containerConfiguration = _fileContainerConfigurationProvider.Get<FileContainerConfiguration>(file.FileContainerName);
        var sourceContainer = _blobContainerFactory.Create(containerConfiguration.AbpBlobContainerName);
        var sourceStream = await sourceContainer.GetAsync(file.BlobName!, CancellationToken);

        // 解码前像素上限，防解压炸弹
        if (!SkiaImageHeaderReader.TryReadInfo(sourceStream, out var sourceWidth, out var sourceHeight, out _) ||
            (long)sourceWidth * sourceHeight > _options.MaxPixelCount)
        {
            await sourceStream.DisposeAsync();
            await UpdateStateAsync(thumbnailId, row => row.MarkFailed(
                $"Pixel count {sourceWidth}x{sourceHeight} exceeds the limit or the header is unreadable"));
            return null;
        }

        return sourceStream;
    }

    /// <summary>
    /// 并发闸门内做缩放 + 压缩。provider 不支持该格式时把映射行落为 Unsupported 并返回 null
    /// （跳过不是失败，不进 ABP 重试循环）；意外异常向上抛给作业重试。
    /// </summary>
    private async Task<Stream?> ProcessAsync(Stream sourceStream, File file, Guid thumbnailId)
    {
        var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant() ?? string.Empty;
        var mimeType = ImageMimeTypes.GetMimeType(extension);

        var (state, processed) = await _throttle.ExecuteAsync(async ct =>
        {
            var resized = await _imageResizer.ResizeAsync(
                sourceStream,
                new ImageResizeArgs(_options.ThumbnailMaxWidth, _options.ThumbnailMaxHeight, ImageResizeMode.Max),
                mimeType,
                ct);

            // Unsupported：provider 不支持（gif/bmp 等），跳过不是失败
            if (resized.State == ImageProcessState.Unsupported)
            {
                return (ImageProcessState.Unsupported, (Stream?)null);
            }

            var compressed = await _imageCompressor.CompressAsync(resized.Result, mimeType, ct);
            if (compressed.State == ImageProcessState.Unsupported)
            {
                await resized.Result.DisposeAsync();
                return (ImageProcessState.Unsupported, (Stream?)null);
            }

            // 压缩成功时结果是独立的新流（ABP ImageCompressor 返回新 MemoryStream，已核实 contributor 源码），
            // 中间的 resize 流在此释放；Canceled = 输出不比输入小，compressed.Result 就是 resize 后的流本身，
            // 绝不能释放——用引用相等把两种情况分开。
            if (compressed.State == ImageProcessState.Done && !ReferenceEquals(compressed.Result, resized.Result))
            {
                await resized.Result.DisposeAsync();
            }

            return (ImageProcessState.Done, (Stream?)compressed.Result);
        }, CancellationToken);

        if (state != ImageProcessState.Done || processed is null)
        {
            await UpdateStateAsync(thumbnailId, row => row.MarkUnsupported("Image provider does not support this format"));
            return null;
        }

        return processed;
    }

    /// <summary>
    /// 读处理结果的头（实际格式与宽高）→ 存缩略图 blob → Done 落库。
    /// 头不可读时落 Failed 并返回 null；成功返回实际写入的 blob 名。
    /// </summary>
    private async Task<string?> PersistThumbnailAsync(Guid thumbnailId, Stream processed, Guid fileId)
    {
        // 输出格式 = 源格式（但 .jpeg 归一为 .jpg）；源文件扩展名可能与内容不符，以处理结果的头部为准
        if (!SkiaImageHeaderReader.TryReadInfo(processed, out var width, out var height, out var processedExtension) ||
            processedExtension is null)
        {
            await UpdateStateAsync(thumbnailId, row => row.MarkFailed("Processed thumbnail header is unreadable"));
            return null;
        }

        var blobName = $"{fileId:N}{processedExtension}";
        await _thumbnailContainer.SaveAsync(blobName, processed, overrideExisting: true, CancellationToken);

        await UpdateStateAsync(thumbnailId, row => row.MarkDone(blobName, width, height));
        return blobName;
    }

    private async Task UpdateStateAsync(Guid thumbnailId, Action<FileThumbnail> change)
    {
        // 状态落库与业务写入分属独立 UoW（00-overview 6.5：失败路径上的记录必须独立提交）
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var row = await _thumbnailRepository.GetAsync(thumbnailId, cancellationToken: CancellationToken);
        change(row);
        await _thumbnailRepository.UpdateAsync(row, cancellationToken: CancellationToken);
        await uow.CompleteAsync();
    }

    private async Task DeleteThumbnailAsync(Guid thumbnailId, string? blobName)
    {
        if (!blobName.IsNullOrWhiteSpace())
        {
            try
            {
                await _thumbnailContainer.DeleteAsync(blobName!, CancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Failed to delete thumbnail blob {BlobName}, continuing with row deletion", blobName);
            }
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        await _thumbnailRepository.DeleteAsync(thumbnailId, cancellationToken: CancellationToken);
        await uow.CompleteAsync();
    }
}
