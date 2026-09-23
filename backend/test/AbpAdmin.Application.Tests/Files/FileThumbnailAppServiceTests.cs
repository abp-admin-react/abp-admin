using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Imaging;
using EasyAbp.FileManagement.Files;
using EasyAbp.FileManagement.Files.Dtos;
using Shouldly;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Files;

/* T3.1 文件缩略图集成测试。
 * 覆盖验收标准：
 * - 上传图片 → 映射行 Pending（事件入队），手动执行作业 → Done + blob 存在 + 宽高受限；
 * - 上传 .txt → 记录为 Unsupported，作业不抛异常（不会进入 ABP 重试循环）；
 * - 手动把行改回 Pending，回填管理器重新入队，作业再跑恢复 Done；
 * - 列表端点下发 thumbnailUrl，GetAsync 返回缩略图内容。
 *
 * 测试环境 AbpBackgroundJobOptions.IsJobExecutionEnabled = false，
 * 作业一律手动解析执行（幂等性也因此可重复验证）。
 */
public abstract class FileThumbnailAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string ContainerName = "admin";

    private readonly IFileAppService _fileAppService;
    private readonly IFileThumbnailAppService _fileThumbnailAppService;
    private readonly IRepository<FileThumbnail, Guid> _thumbnailRepository;
    private readonly IRepository<BackgroundJobRecord, Guid> _backgroundJobRepository;
    private readonly IBlobContainer<FileThumbnailContainer> _thumbnailContainer;

    protected FileThumbnailAppServiceTests()
    {
        _fileAppService = GetRequiredService<IFileAppService>();
        _fileThumbnailAppService = GetRequiredService<IFileThumbnailAppService>();
        _thumbnailRepository = GetRequiredService<IRepository<FileThumbnail, Guid>>();
        _backgroundJobRepository = GetRequiredService<IRepository<BackgroundJobRecord, Guid>>();
        _thumbnailContainer = GetRequiredService<IBlobContainer<FileThumbnailContainer>>();
    }

    [Fact]
    public async Task Should_Enqueue_On_Upload_And_Generate_Thumbnail()
    {
        var fileId = await UploadAsync(TestImages.CreatePng(800, 600), "photo.png", "image/png");

        // 上传请求只建行 + 入队，不同步生成（State 仍是 Pending）
        var thumbnail = await GetThumbnailAsync(fileId);
        thumbnail.State.ShouldBe(FileThumbnailStateEnum.Pending);

        await ExecuteJobAsync(fileId, "photo.png");

        thumbnail = await GetThumbnailAsync(fileId);
        thumbnail.State.ShouldBe(FileThumbnailStateEnum.Done);
        thumbnail.BlobName.ShouldNotBeNullOrWhiteSpace();
        thumbnail.Width.ShouldBeLessThanOrEqualTo(256);
        thumbnail.Height.ShouldBeLessThanOrEqualTo(256);

        // blob 存在且是可解码图片，尺寸与记录一致
        var blobBytes = await _thumbnailContainer.GetAllBytesAsync(thumbnail.BlobName!);
        SkiaImageHeaderReader.TryReadInfo(new MemoryStream(blobBytes), out var width, out var height, out _)
            .ShouldBeTrue();
        width.ShouldBe(thumbnail.Width);
        height.ShouldBe(thumbnail.Height);

        // 幂等：再执行一次仍是 Done
        await ExecuteJobAsync(fileId, "photo.png");
        (await GetThumbnailAsync(fileId)).State.ShouldBe(FileThumbnailStateEnum.Done);

        // 列表端点下发 thumbnailUrl
        var list = await _fileThumbnailAppService.GetListWithThumbnailsAsync(new GetFileListInput
        {
            FileContainerName = ContainerName
        });
        list.ThumbnailUrls.ShouldContainKey(fileId);
        list.ThumbnailUrls[fileId].ShouldBe($"/api/app/file-thumbnail/{fileId}");

        // GetAsync 返回缩略图内容
        var content = await _fileThumbnailAppService.GetAsync(fileId);
        content.ShouldNotBeNull();
        using var stream = content.GetStream();
        SkiaImageHeaderReader.TryReadInfo(stream, out _, out _, out _).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Mark_Unsupported_For_Txt_Without_Entering_Retry_Loop()
    {
        var fileId = await UploadAsync("hello, not an image"u8.ToArray(), "note.txt", "text/plain");

        // 作业正常返回（不抛异常 → ABP 后台作业不会重试）
        await ExecuteJobAsync(fileId, "note.txt");

        var thumbnail = await GetThumbnailAsync(fileId);
        thumbnail.State.ShouldBe(FileThumbnailStateEnum.Unsupported);

        // AbpBackgroundJobs 表里该作业没有 TryCount > 1 的记录
        await WithUnitOfWorkAsync(async () =>
        {
            var jobs = await _backgroundJobRepository.GetListAsync(
                x => x.JobName == typeof(ThumbnailGenerationJob).FullName);
            jobs.ShouldAllBe(job => job.TryCount <= 1);
        });
    }

    [Fact]
    public async Task Backfill_Should_Reenqueue_Pending_And_Missing_Rows()
    {
        var fileId = await UploadAsync(TestImages.CreatePng(400, 400), "backfill.png", "image/png");
        await ExecuteJobAsync(fileId, "backfill.png");
        (await GetThumbnailAsync(fileId)).State.ShouldBe(FileThumbnailStateEnum.Done);

        var backfillManager = GetRequiredService<FileThumbnailBackfillManager>();

        // 1) 手动把行改回 Pending → 回填重新入队 → 作业再跑恢复 Done
        await WithUnitOfWorkAsync(async () =>
        {
            var row = await _thumbnailRepository.GetAsync(x => x.FileId == fileId);
            row.ResetToPending();
            await _thumbnailRepository.UpdateAsync(row);
        });

        (await WithUnitOfWorkAsync(() => backfillManager.EnqueuePendingAndMissingAsync()))
            .ShouldBeGreaterThanOrEqualTo(1);
        await ExecuteJobAsync(fileId, "backfill.png");
        (await GetThumbnailAsync(fileId)).State.ShouldBe(FileThumbnailStateEnum.Done);

        // 2) 没有映射行的图片文件 → 回填建行 + 入队
        var missingFileId = await UploadAsync(TestImages.CreatePng(200, 200), "missing.png", "image/png");
        await WithUnitOfWorkAsync(async () =>
        {
            var row = await _thumbnailRepository.GetAsync(x => x.FileId == missingFileId);
            await _thumbnailRepository.DeleteAsync(row);
        });

        (await WithUnitOfWorkAsync(() => backfillManager.EnqueuePendingAndMissingAsync()))
            .ShouldBeGreaterThanOrEqualTo(1);

        var recreated = await GetThumbnailAsync(missingFileId);
        recreated.State.ShouldBe(FileThumbnailStateEnum.Pending);

        await ExecuteJobAsync(missingFileId, "missing.png");
        (await GetThumbnailAsync(missingFileId)).State.ShouldBe(FileThumbnailStateEnum.Done);
    }

    private async Task<Guid> UploadAsync(byte[] content, string fileName, string mimeType)
    {
        var created = await _fileAppService.CreateAsync(new CreateFileInput
        {
            FileContainerName = ContainerName,
            FileName = fileName,
            MimeType = mimeType,
            FileType = FileType.RegularFile,
            Content = content
        });
        return created.FileInfo.Id;
    }

    private async Task<FileThumbnail> GetThumbnailAsync(Guid fileId)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var row = await _thumbnailRepository.FindAsync(x => x.FileId == fileId);
            row.ShouldNotBeNull($"FileThumbnail row for {fileId} should exist");
            return row!;
        });
    }

    private async Task ExecuteJobAsync(Guid fileId, string fileName)
    {
        var job = GetRequiredService<ThumbnailGenerationJob>();
        await job.ExecuteAsync(new ThumbnailGenerationJobArgs
        {
            TenantId = null,
            FileId = fileId,
            FileName = fileName
        });
    }
}
