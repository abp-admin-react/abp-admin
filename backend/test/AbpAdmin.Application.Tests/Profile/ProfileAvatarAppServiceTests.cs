using System;
using System.IO;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Imaging;
using Shouldly;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Profile;

/* T3.1 头像上传链路集成测试。
 * 覆盖验收标准：
 * - .exe 改名 .png → 业务异常且 BLOB 容器里没有产生任何文件；
 * - 真实 PNG 改名 .jpg → 同样被拒；
 * - 超限文件 → AvatarTooLarge；
 * - 大图上传 → 落库 256x256 且字节数显著小于原图；
 * - 头像版本号（?v=）与删除；
 * - 跨租户读取保护（多租户验收：host 与租户两种上下文）。
 *
 * 测试环境 FakeCurrentPrincipalAccessor 固定当前用户为 host admin
 * (Id: 2e701e62-0953-4dd3-910b-dc6cc93ccb0d)，该用户在 AbpUsers 里默认不存在，
 * 用例里按需补建（头像元信息要落 ExtraProperties）。
 */
public abstract class ProfileAvatarAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly IProfileAvatarAppService _profileAvatarAppService;
    private readonly IBlobContainer<AvatarContainer> _avatarContainer;
    private readonly IdentityUserManager _userManager;
    private readonly IIdentityUserRepository _identityUserRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly TenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;

    protected ProfileAvatarAppServiceTests()
    {
        _profileAvatarAppService = GetRequiredService<IProfileAvatarAppService>();
        _avatarContainer = GetRequiredService<IBlobContainer<AvatarContainer>>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _identityUserRepository = GetRequiredService<IIdentityUserRepository>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _currentPrincipalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _tenantManager = GetRequiredService<TenantManager>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
    }

    [Fact]
    public async Task Should_Upload_Png_Avatar_And_Get_Resized_Result()
    {
        await EnsureHostAdminExistsAsync();
        var sourceBytes = TestImages.CreatePng(800, 600);

        await _profileAvatarAppService.UploadAsync(new UploadAvatarInput
        {
            File = new RemoteStreamContent(new MemoryStream(sourceBytes), "avatar.png", "image/png", sourceBytes.Length)
        });

        // blob 落库（格式保持 PNG，见 AppService 注释：ABP contributor 不转码）
        var blobName = $"{AdminUserId:N}.png";
        (await _avatarContainer.ExistsAsync(blobName)).ShouldBeTrue();
        var avatarBytes = await _avatarContainer.GetAllBytesAsync(blobName);

        // 落库的是 256x256，字节数显著小于原图
        SkiaImageHeaderReader.TryReadInfo(new MemoryStream(avatarBytes), out var width, out var height, out _)
            .ShouldBeTrue();
        width.ShouldBe(256);
        height.ShouldBe(256);
        avatarBytes.Length.ShouldBeLessThan(sourceBytes.Length);

        // 版本号写入 ExtraProperties，头像信息端点下发带 v 的 URL
        var user = await _identityUserRepository.GetAsync(AdminUserId);
        var version = user.GetProperty<string?>(AbpAdminConsts.AvatarVersionPropertyName, null);
        version.ShouldNotBeNullOrWhiteSpace();

        var info = await _profileAvatarAppService.GetMyAvatarInfoAsync();
        info.AvatarUrl.ShouldBe($"/api/app/profile-avatar/{AdminUserId}?v={version}");

        // GetAsync 读回内容
        var content = await _profileAvatarAppService.GetAsync(AdminUserId);
        content.ShouldNotBeNull();
        content!.ContentType.ShouldBe("image/png");
        using var readBack = content.GetStream();
        SkiaImageHeaderReader.TryReadInfo(readBack, out var readWidth, out var readHeight, out _).ShouldBeTrue();
        readWidth.ShouldBe(256);
        readHeight.ShouldBe(256);
    }

    [Fact]
    public async Task Should_Upload_4000x3000_Jpeg_And_Get_256x256_Avatar()
    {
        await EnsureHostAdminExistsAsync();
        var sourceBytes = TestImages.CreateJpeg(4000, 3000);

        await _profileAvatarAppService.UploadAsync(new UploadAvatarInput
        {
            File = new RemoteStreamContent(new MemoryStream(sourceBytes), "photo.jpg", "image/jpeg", sourceBytes.Length)
        });

        var blobName = $"{AdminUserId:N}.jpg";
        (await _avatarContainer.ExistsAsync(blobName)).ShouldBeTrue();
        var avatarBytes = await _avatarContainer.GetAllBytesAsync(blobName);

        SkiaImageHeaderReader.TryReadInfo(new MemoryStream(avatarBytes), out var width, out var height, out var extension)
            .ShouldBeTrue();
        width.ShouldBe(256);
        height.ShouldBe(256);
        extension.ShouldBe(".jpg");
        avatarBytes.Length.ShouldBeLessThan(sourceBytes.Length);
    }

    [Fact]
    public async Task Should_Reject_Exe_Renamed_As_Png_And_Write_Nothing_To_Blob()
    {
        await EnsureHostAdminExistsAsync();

        var exception = await Should.ThrowAsync<BusinessException>(
            _profileAvatarAppService.UploadAsync(new UploadAvatarInput
            {
                File = new RemoteStreamContent(new MemoryStream(TestImages.ExeBytes), "evil.png", "image/png")
            }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Imaging.InvalidImageContent);

        // BLOB 容器里没有产生任何文件
        (await _avatarContainer.ExistsAsync($"{AdminUserId:N}.png")).ShouldBeFalse();
        (await _avatarContainer.ExistsAsync($"{AdminUserId:N}.jpg")).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Reject_Real_Png_Renamed_As_Jpg()
    {
        await EnsureHostAdminExistsAsync();
        var pngBytes = TestImages.CreatePng(64, 64);

        var exception = await Should.ThrowAsync<BusinessException>(
            _profileAvatarAppService.UploadAsync(new UploadAvatarInput
            {
                File = new RemoteStreamContent(new MemoryStream(pngBytes), "fake.jpg", "image/jpeg")
            }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Imaging.InvalidImageContent);
        (await _avatarContainer.ExistsAsync($"{AdminUserId:N}.jpg")).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Reject_Extension_Outside_Whitelist()
    {
        await EnsureHostAdminExistsAsync();
        var pngBytes = TestImages.CreatePng(64, 64);

        var exception = await Should.ThrowAsync<BusinessException>(
            _profileAvatarAppService.UploadAsync(new UploadAvatarInput
            {
                File = new RemoteStreamContent(new MemoryStream(pngBytes), "avatar.gif", "image/gif")
            }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Imaging.InvalidImageExtension);
    }

    [Fact]
    public async Task Should_Reject_Oversize_Avatar()
    {
        await EnsureHostAdminExistsAsync();
        // 6 MiB 随机字节（合法格式与否不重要，大小闸门先触发；ContentLength 故意不设，
        // 覆盖「客户端谎报/不报 Content-Length 时读取期硬上限兜住」的路径）
        var bigBytes = new byte[6 * 1024 * 1024];
        new Random(42).NextBytes(bigBytes);

        var exception = await Should.ThrowAsync<BusinessException>(
            _profileAvatarAppService.UploadAsync(new UploadAvatarInput
            {
                File = new RemoteStreamContent(new MemoryStream(bigBytes), "big.jpg", "image/jpeg")
            }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Imaging.AvatarTooLarge);
    }

    [Fact]
    public async Task Should_Delete_Avatar()
    {
        await EnsureHostAdminExistsAsync();
        var sourceBytes = TestImages.CreatePng(300, 300);
        await _profileAvatarAppService.UploadAsync(new UploadAvatarInput
        {
            File = new RemoteStreamContent(new MemoryStream(sourceBytes), "avatar.png", "image/png", sourceBytes.Length)
        });
        (await _profileAvatarAppService.GetMyAvatarInfoAsync()).AvatarUrl.ShouldNotBeNull();

        await _profileAvatarAppService.DeleteAsync();

        (await _profileAvatarAppService.GetMyAvatarInfoAsync()).AvatarUrl.ShouldBeNull();
        (await _avatarContainer.ExistsAsync($"{AdminUserId:N}.png")).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Not_Probe_Avatar_Across_Tenants()
    {
        // 租户上下文：建租户 + 租户用户，以租户用户身份上传头像
        var tenant = await _tenantManager.CreateAsync($"avatar-tenant-{Guid.NewGuid():N}"[..20]);
        await WithUnitOfWorkAsync(async () =>
        {
            await _tenantRepository.InsertAsync(tenant);
        });

        IdentityUser tenantUser;
        using (_currentTenant.Change(tenant.Id))
        {
            tenantUser = new IdentityUser(Guid.NewGuid(), "tenant-avatar-user", "tenant-avatar@test.local", tenant.Id);
            (await _userManager.CreateAsync(tenantUser, "Test@123456")).Succeeded.ShouldBeTrue();

            var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(AbpClaimTypes.UserId, tenantUser.Id.ToString()),
                new Claim(AbpClaimTypes.UserName, tenantUser.UserName!)
            ]));
            using (_currentPrincipalAccessor.Change(principal))
            {
                var sourceBytes = TestImages.CreatePng(300, 300);
                await _profileAvatarAppService.UploadAsync(new UploadAvatarInput
                {
                    File = new RemoteStreamContent(new MemoryStream(sourceBytes), "avatar.png", "image/png", sourceBytes.Length)
                });

                // 租户上下文里能读到自己的头像
                (await _profileAvatarAppService.GetAsync(tenantUser.Id)).ShouldNotBeNull();
            }
        }

        // host 上下文：同一 userId 探测不到（用户仓储按租户过滤 + BLOB 容器按租户隔离）
        (await _profileAvatarAppService.GetAsync(tenantUser.Id)).ShouldBeNull();
    }

    private async Task EnsureHostAdminExistsAsync()
    {
        if (await _userManager.FindByIdAsync(AdminUserId.ToString()) is not null)
        {
            return;
        }

        // 种子库里已有名为 "admin" 的用户（Id 不同），这里以 FakeCurrentPrincipalAccessor 的
        // 固定 Id 补建一个不同用户名的用户，供头像元信息（ExtraProperties）落库
        var user = new IdentityUser(AdminUserId, "host-admin-avatar", "host-admin-avatar@test.local");
        (await _userManager.CreateAsync(user, "1q2w3E*")).Succeeded.ShouldBeTrue();
    }
}
