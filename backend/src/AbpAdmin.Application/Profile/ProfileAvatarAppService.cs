using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Imaging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.BlobStoring;
using Volo.Abp.Content;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Imaging;
using Volo.Abp.Users;

namespace AbpAdmin.Profile;

/// <summary>
/// 头像上传链路（T3.1）：扩展名白名单 → 大小上限 → magic bytes → 解码前像素上限 →
/// 缩放（Crop 正方形）→ 压缩 → 写 BLOB。顺序不能变。
///
/// 两个与规格文本的偏差（均已在规格允许范围内，注释备查）：
/// 1. blob 名不是固定 {userId:N}.jpg——ABP SkiaSharp contributor 不转码（输出保持源格式，
///    已核实源码），所以 blob 名按实际格式落 {userId:N}{ext}，上传成功后清理其余扩展名的旧 blob，
///    消除多扩展名残留的语义不变。
/// 2. 压缩结果 State == Canceled 不是失败：输出不比输入小时 contributor 返回 Canceled +
///    原流（已核实源码），Result 正常可用；只有 Unsupported 才视为失败（magic bytes 已确认
///    是 JPEG/PNG 时 provider 说不支持 = 配置问题）。
/// </summary>
[Authorize]
public class ProfileAvatarAppService : AbpAdminAppService, IProfileAvatarAppService
{
    private readonly IImageContentValidator _imageContentValidator;
    private readonly IImageResizer _imageResizer;
    private readonly IImageCompressor _imageCompressor;
    private readonly IBlobContainer<AvatarContainer> _avatarContainer;
    private readonly IIdentityUserRepository _identityUserRepository;
    private readonly IImageProcessingThrottle _throttle;
    private readonly AbpAdminImagingOptions _options;

    public ProfileAvatarAppService(
        IImageContentValidator imageContentValidator,
        IImageResizer imageResizer,
        IImageCompressor imageCompressor,
        IBlobContainer<AvatarContainer> avatarContainer,
        IIdentityUserRepository identityUserRepository,
        IOptions<AbpAdminImagingOptions> options,
        IImageProcessingThrottle throttle)
    {
        _imageContentValidator = imageContentValidator;
        _imageResizer = imageResizer;
        _imageCompressor = imageCompressor;
        _avatarContainer = avatarContainer;
        _identityUserRepository = identityUserRepository;
        _throttle = throttle;
        _options = options.Value;
    }

    // 显式声明吃 multipart/form-data：ABP 约定路由对 DTO 内嵌 IRemoteStreamContent
    // 只放 application/json 进 Consumes（见 swagger），不声明时浏览器传的 multipart 被 415 短路的。
    // 绑定本身由 AbpRemoteStreamContentModelBinder 从 Request.Form.Files 完成，字段名 File。
    [Consumes("multipart/form-data")]
    public virtual async Task UploadAsync(UploadAvatarInput input)
    {
        var userId = CurrentUser.GetId();
        var extension = Path.GetExtension(input.File.FileName ?? string.Empty).ToLowerInvariant();

        // 1. 大小上限。先看 ContentLength（可能为 null 或被谎报），读取时仍按硬上限兜住。
        if (input.File.ContentLength is > 0 && input.File.ContentLength > _options.AvatarMaxByteSize)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Imaging.AvatarTooLarge)
                .WithData("MaxSize", _options.AvatarMaxByteSize);
        }

        // 2. 整体读进内存（上限 5 MiB，可接受），得到可 Seek 的流。
        //    LimitedCopyToAsync 超限即抛，不会先读完再判断（见该方法的注释）。
        await using var buffer = new MemoryStream();
        await input.File.GetStream().LimitedCopyToAsync(buffer, _options.AvatarMaxByteSize);
        buffer.Seek(0, SeekOrigin.Begin);

        // 3. 扩展名白名单 + magic bytes 内容校验（双向约束）
        await _imageContentValidator.ValidateAsync(buffer, extension, _options.AvatarAllowedExtensions);

        // 4. 解码前像素上限，防解压炸弹（SKCodec 只读文件头，已验证可行）
        if (!SkiaImageHeaderReader.TryReadInfo(buffer, out var sourceWidth, out var sourceHeight, out _) ||
            (long)sourceWidth * sourceHeight > _options.MaxPixelCount)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Imaging.InvalidImageContent)
                .WithData("Reason", $"PixelCount {sourceWidth}x{sourceHeight}");
        }

        var mimeType = ImageMimeTypes.GetMimeType(extension);

        // 5. 缩放 + 压缩（受并发闸门与超时保护）
        var processed = await _throttle.ExecuteAsync(async ct =>
        {
            var resized = await _imageResizer.ResizeAsync(
                buffer,
                new ImageResizeArgs(_options.AvatarSize, _options.AvatarSize, ImageResizeMode.Crop),
                mimeType,
                ct);

            EnsureProcessed(resized.State);
            await using (resized.Result)
            {
                var compressed = await _imageCompressor.CompressAsync(resized.Result, mimeType, ct);
                EnsureProcessed(compressed.State);
                return compressed.Result;
            }
        });

        // 6. 写 BLOB，覆盖旧文件；blob 名取处理结果的实际格式
        await using (processed)
        {
            var blobExtension = SkiaImageHeaderReader.TryReadInfo(processed, out _, out _, out var actualExtension) &&
                                actualExtension is not null
                ? actualExtension
                : ImageMimeTypes.NormalizeImageExtension(extension);

            await _avatarContainer.SaveAsync(AvatarBlobNames.ForUser(userId, blobExtension), processed, overrideExisting: true);

            // 换格式上传时清掉旧扩展名的 blob，消除残留
            foreach (var staleExtension in AvatarBlobNames.CandidateExtensions(_options).Where(e => e != blobExtension))
            {
                await _avatarContainer.DeleteAsync(AvatarBlobNames.ForUser(userId, staleExtension));
            }

            // 7. 更新头像版本号（前端用 ?v={version} 击穿缓存）并持久化实际落盘扩展名
            //（GDPR 导出/删户清理据此精确删除，白名单收窄不残留）
            await SaveAvatarMetadataAsync(userId, blobExtension);
        }
    }

    public virtual async Task DeleteAsync()
    {
        var userId = CurrentUser.GetId();

        foreach (var extension in AvatarBlobNames.CandidateExtensions(_options))
        {
            await _avatarContainer.DeleteAsync(AvatarBlobNames.ForUser(userId, extension));
        }

        var user = await _identityUserRepository.GetAsync(userId);
        user.RemoveProperty(AbpAdminConsts.AvatarVersionPropertyName);
        user.RemoveProperty(AbpAdminConsts.AvatarBlobExtensionPropertyName);
        await _identityUserRepository.UpdateAsync(user);
    }

    public virtual async Task<IRemoteStreamContent?> GetAsync(Guid id)
    {
        // 必须校验 id 属于当前租户（仓储带租户过滤），防止跨租户探测
        var user = await _identityUserRepository.FindAsync(id);
        if (user is null)
        {
            return null;
        }

        var storedExtension = user.GetProperty<string?>(AbpAdminConsts.AvatarBlobExtensionPropertyName, null);
        var found = await AvatarBlobReader.FindFirstAsync(_avatarContainer, _options, id, storedExtension);
        if (found is not null)
        {
            var extension = found.Value.Extension;
            var bytes = found.Value.Bytes;

            // 缓存头：头像内容按版本变化，URL 带 v 参数，可安全长缓存（策略常量见 AvatarCacheConsts）。
            // IHttpContextAccessor 惰性解析：非 Web 宿主（集成测试直接调 AppService）没有注册，跳过即可。
            // TODO（重构报告问题 18）：缓存头属 Http 关注点，理想位置是 HttpApi 层包装器；
            //   当前头像走动态 API 无独立控制器，下沉需新增 Host 侧实现（IAvatarCachePolicyApplier），待后续处理。
            var httpContext = LazyServiceProvider.LazyGetService<IHttpContextAccessor>()?.HttpContext;
            if (httpContext is not null)
            {
                httpContext.Response.Headers.CacheControl = AvatarCacheConsts.CacheControlHeaderValue;
                var version = user.GetProperty<string?>(AbpAdminConsts.AvatarVersionPropertyName, null);
                if (!version.IsNullOrWhiteSpace())
                {
                    httpContext.Response.Headers.ETag = $"\"{version}\"";
                }
            }

            return new RemoteStreamContent(
                new MemoryStream(bytes),
                fileName: $"avatar{extension}",
                contentType: ImageMimeTypes.GetMimeType(extension) ?? "application/octet-stream",
                readOnlyLength: bytes.Length);
        }

        return null;
    }

    public virtual async Task<AvatarInfoDto> GetMyAvatarInfoAsync()
    {
        var userId = CurrentUser.GetId();
        var user = await _identityUserRepository.FindAsync(userId);
        var version = user?.GetProperty<string?>(AbpAdminConsts.AvatarVersionPropertyName, null);

        return new AvatarInfoDto
        {
            AvatarUrl = version.IsNullOrWhiteSpace()
                ? null
                : $"/api/app/profile-avatar/{userId}?v={version}"
        };
    }

    private void EnsureProcessed(ImageProcessState state)
    {
        if (state == ImageProcessState.Unsupported)
        {
            // magic bytes 已确认是白名单格式，provider 还说不支持 = 服务端配置问题，视为失败
            Logger.LogWarning("Image provider returned Unsupported for a magic-bytes-validated avatar");
            throw new BusinessException(AbpAdminDomainErrorCodes.Imaging.InvalidImageContent)
                .WithData("Reason", "ProviderUnsupported");
        }
        // Canceled = 压缩输出不比输入小，Result 是 resize 后的流，正常可用
    }

    private async Task SaveAvatarMetadataAsync(Guid userId, string blobExtension)
    {
        var user = await _identityUserRepository.GetAsync(userId);
        user.SetProperty(AbpAdminConsts.AvatarVersionPropertyName, Guid.NewGuid().ToString("N")[..8]);
        user.SetProperty(AbpAdminConsts.AvatarBlobExtensionPropertyName, blobExtension);
        await _identityUserRepository.UpdateAsync(user);
    }
}
