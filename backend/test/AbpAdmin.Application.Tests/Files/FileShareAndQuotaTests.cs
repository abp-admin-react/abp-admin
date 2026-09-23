using System;
using System.IO;
using System.Threading.Tasks;
using AbpAdmin.Features;
using EasyAbp.FileManagement.Files;
using EasyAbp.FileManagement.Files.Dtos;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Features;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Volo.Abp.Validation;
using Xunit;

namespace AbpAdmin.Files;

public abstract class FileShareAndQuotaTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string ContainerName = "admin";

    [Fact]
    public async Task Should_Create_Share_And_Download_By_Token()
    {
        var fileAppService = GetRequiredService<IFileAppService>();
        var shareAppService = GetRequiredService<IFileShareAppService>();

        var created = await fileAppService.CreateAsync(new CreateFileInput
        {
            FileContainerName = ContainerName,
            FileName = "share-note.txt",
            MimeType = "text/plain",
            FileType = FileType.RegularFile,
            Content = "shared"u8.ToArray()
        });

        var share = await shareAppService.CreateAsync(new CreateFileShareLinkInput
        {
            FileId = created.FileInfo.Id,
            IsPublic = true,
            MaxDownloads = 1
        });

        share.Token.ShouldNotBeNullOrWhiteSpace();

        var downloaded = await shareAppService.DownloadByTokenAsync(share.Token);
        downloaded.FileName.ShouldBe("share-note.txt");

        await Should.ThrowAsync<BusinessException>(() =>
            shareAppService.DownloadByTokenAsync(share.Token));
    }

    [Fact]
    public async Task Should_Reject_Unknown_Share_Token()
    {
        var shareAppService = GetRequiredService<IFileShareAppService>();
        var ex = await Should.ThrowAsync<BusinessException>(() =>
            shareAppService.DownloadByTokenAsync("no-such-token"));
        ex.Code.ShouldBe(AbpAdminDomainErrorCodes.Files.ShareLinkExpired);
    }

    [Fact]
    public async Task Should_Reject_Non_Public_Share_Token()
    {
        var fileAppService = GetRequiredService<IFileAppService>();
        var shareAppService = GetRequiredService<IFileShareAppService>();
        var repository = GetRequiredService<IRepository<FileShareLink, Guid>>();

        var created = await fileAppService.CreateAsync(new CreateFileInput
        {
            FileContainerName = ContainerName,
            FileName = "private-share.txt",
            MimeType = "text/plain",
            FileType = FileType.RegularFile,
            Content = "x"u8.ToArray()
        });

        var entity = new FileShareLink(
            Guid.NewGuid(),
            null,
            created.FileInfo.Id,
            "private-token-01234567890123456789012345678901",
            DateTime.UtcNow.AddDays(1),
            isPublic: false,
            maxDownloads: null);

        await WithUnitOfWorkAsync(() => repository.InsertAsync(entity, autoSave: true));

        var ex = await Should.ThrowAsync<BusinessException>(() =>
            shareAppService.DownloadByTokenAsync(entity.Token));
        ex.Code.ShouldBe(AbpAdminDomainErrorCodes.Files.ShareLinkExpired);
    }

    [Fact]
    public async Task Should_Reject_Zero_MaxDownloads_On_Create()
    {
        var fileAppService = GetRequiredService<IFileAppService>();
        var shareAppService = GetRequiredService<IFileShareAppService>();

        var created = await fileAppService.CreateAsync(new CreateFileInput
        {
            FileContainerName = ContainerName,
            FileName = "zero-max.txt",
            MimeType = "text/plain",
            FileType = FileType.RegularFile,
            Content = "x"u8.ToArray()
        });

        await Should.ThrowAsync<AbpValidationException>(() =>
            shareAppService.CreateAsync(new CreateFileShareLinkInput
            {
                FileId = created.FileInfo.Id,
                IsPublic = true,
                MaxDownloads = 0
            }));
    }

    [Fact]
    public async Task Should_Reject_Non_Public_And_Too_Long_Expire_On_Create()
    {
        var fileAppService = GetRequiredService<IFileAppService>();
        var shareAppService = GetRequiredService<IFileShareAppService>();
        var clock = GetRequiredService<IClock>();

        var created = await fileAppService.CreateAsync(new CreateFileInput
        {
            FileContainerName = ContainerName,
            FileName = "invalid-share.txt",
            MimeType = "text/plain",
            FileType = FileType.RegularFile,
            Content = "x"u8.ToArray()
        });

        var notPublic = await Should.ThrowAsync<BusinessException>(() =>
            shareAppService.CreateAsync(new CreateFileShareLinkInput
            {
                FileId = created.FileInfo.Id,
                IsPublic = false
            }));
        notPublic.Code.ShouldBe(AbpAdminDomainErrorCodes.Files.ShareLinkInvalid);

        var tooLong = await Should.ThrowAsync<BusinessException>(() =>
            shareAppService.CreateAsync(new CreateFileShareLinkInput
            {
                FileId = created.FileInfo.Id,
                IsPublic = true,
                ExpireTime = clock.Now.AddDays(FileShareLinkConsts.MaxExpireDays + 1)
            }));
        tooLong.Code.ShouldBe(AbpAdminDomainErrorCodes.Files.ShareLinkInvalid);
    }

    [Fact]
    public void Should_Reject_Non_Positive_MaxDownloads_On_Entity()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FileShareLink(
            Guid.NewGuid(),
            null,
            Guid.NewGuid(),
            "ctor-token-012345678901234567890123456789012",
            DateTime.UtcNow.AddDays(1),
            true,
            0));
    }

    [Fact]
    public async Task Should_Reject_Expired_Share_Token()
    {
        var fileAppService = GetRequiredService<IFileAppService>();
        var shareAppService = GetRequiredService<IFileShareAppService>();
        var repository = GetRequiredService<IRepository<FileShareLink, Guid>>();

        var created = await fileAppService.CreateAsync(new CreateFileInput
        {
            FileContainerName = ContainerName,
            FileName = "expired-share.txt",
            MimeType = "text/plain",
            FileType = FileType.RegularFile,
            Content = "x"u8.ToArray()
        });

        var entity = new FileShareLink(
            Guid.NewGuid(),
            null,
            created.FileInfo.Id,
            "expiredtoken012345678901234567890123456789012",
            DateTime.UtcNow.AddMinutes(-5),
            true,
            null);

        await WithUnitOfWorkAsync(() => repository.InsertAsync(entity, autoSave: true));

        await Should.ThrowAsync<BusinessException>(() =>
            shareAppService.DownloadByTokenAsync(entity.Token));
    }

    [Fact]
    public async Task Should_Not_Consume_Download_Count_When_Blob_Missing()
    {
        var fileAppService = GetRequiredService<IFileAppService>();
        var shareAppService = GetRequiredService<IFileShareAppService>();
        var fileRepository = GetRequiredService<IFileRepository>();
        var shareRepository = GetRequiredService<IRepository<FileShareLink, Guid>>();
        var blobContainer = GetRequiredService<IBlobContainer<AdminFileBlobContainer>>();

        var content = "rescued"u8.ToArray();
        var created = await fileAppService.CreateAsync(new CreateFileInput
        {
            FileContainerName = ContainerName,
            FileName = "missing-blob.txt",
            MimeType = "text/plain",
            FileType = FileType.RegularFile,
            Content = content
        });

        var share = await shareAppService.CreateAsync(new CreateFileShareLinkInput
        {
            FileId = created.FileInfo.Id,
            IsPublic = true,
            MaxDownloads = 1
        });

        // 直接删掉底层 blob，模拟 provider 故障/对象丢失
        var blobName = await WithUnitOfWorkAsync(async () =>
            (await fileRepository.GetAsync(created.FileInfo.Id)).BlobName);
        blobName.ShouldNotBeNullOrWhiteSpace();
        await blobContainer.DeleteAsync(blobName!);

        // 显式非事务 UoW（与生产 GET 请求一致）：扣次数与回滚都会即时落库，
        // 回滚逻辑缺失时 DownloadCount 会留 1，用例失败
        var unitOfWorkManager = GetRequiredService<IUnitOfWorkManager>();
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var ex = await Should.ThrowAsync<BusinessException>(() =>
                shareAppService.DownloadByTokenAsync(share.Token));
            ex.Code.ShouldBe(AbpAdminDomainErrorCodes.Files.ShareLinkExpired);
            await uow.CompleteAsync();
        }

        var countAfterFailure = await WithUnitOfWorkAsync(async () =>
            (await shareRepository.GetAsync(share.Id)).DownloadCount);
        countAfterFailure.ShouldBe(0);

        // blob 恢复后，MaxDownloads=1 的链接仍然可用（没有被那次失败作废）
        await blobContainer.SaveAsync(blobName!, new MemoryStream(content), overrideExisting: true);
        var downloaded = await shareAppService.DownloadByTokenAsync(share.Token);
        downloaded.FileName.ShouldBe("missing-blob.txt");

        var countAfterSuccess = await WithUnitOfWorkAsync(async () =>
            (await shareRepository.GetAsync(share.Id)).DownloadCount);
        countAfterSuccess.ShouldBe(1);

        // 用尽后不可再下
        await Should.ThrowAsync<BusinessException>(() =>
            shareAppService.DownloadByTokenAsync(share.Token));
    }

    [Fact]
    public async Task Should_Reject_Upload_When_Quota_Exceeded()
    {
        var tenantId = Guid.NewGuid();
        var currentTenant = GetRequiredService<ICurrentTenant>();
        var featureManager = GetRequiredService<IFeatureManager>();
        var checker = GetRequiredService<FileStorageQuotaChecker>();

        using (currentTenant.Change(tenantId))
        {
            await featureManager.SetAsync(
                AbpAdminFeatures.FileManagementStorageQuotaBytes,
                "8",
                TenantFeatureValueProvider.ProviderName,
                tenantId.ToString());

            var ex = await Should.ThrowAsync<BusinessException>(() => checker.CheckAsync(10));
            ex.Code.ShouldBe(AbpAdminDomainErrorCodes.Files.StorageQuotaExceeded);
        }
    }
}
