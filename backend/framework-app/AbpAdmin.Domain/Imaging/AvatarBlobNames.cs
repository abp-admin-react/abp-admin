using System;
using System.Linq;

namespace AbpAdmin.Imaging;

/// <summary>
/// 头像 blob 命名约定（{userId:N}{ext}）的单一来源：上传（ProfileAvatarAppService）、
/// GDPR 导出 Provider、删户清理三处共用。扩展名候选统一从 AbpAdminImagingOptions
/// 白名单归一化——blob 扩展名按实际格式落盘（ABP SkiaSharp contributor 不转码），
/// 历史上可能残留多个扩展名，清理/导出必须遍历全部候选。
/// </summary>
public static class AvatarBlobNames
{
    public static string ForUser(Guid userId, string extension)
    {
        return $"{userId:N}{extension}";
    }

    /// <summary>头像可能出现的 blob 扩展名候选（白名单归一化去重）。</summary>
    public static string[] CandidateExtensions(AbpAdminImagingOptions options)
    {
        return options.AvatarAllowedExtensions
            .Select(ImageMimeTypes.NormalizeImageExtension)
            .Distinct()
            .ToArray();
    }
}
