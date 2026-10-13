using System.Collections.Generic;

namespace AbpAdmin.Imaging;

/// <summary>
/// 图片扩展名 ↔ MIME 映射（T3.1）。头像、缩略图、内容类型响应共用这一份，
/// 不要在各处散落 if/switch。
/// </summary>
public static class ImageMimeTypes
{
    private static readonly IReadOnlyDictionary<string, string> MimeByExtension = new Dictionary<string, string>
    {
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".bmp"] = "image/bmp",
    };

    /// <summary>扩展名归一化（小写、带点）后取 MIME；不认识的扩展名返回 null。</summary>
    public static string? GetMimeType(string extension)
    {
        return MimeByExtension.GetValueOrDefault(FileSignaturesImageContentValidator.NormalizeExtension(extension));
    }

    /// <summary>把 .jpeg 归一到 .jpg（blob 命名用），其余原样返回（小写含点）。</summary>
    public static string NormalizeImageExtension(string extension)
    {
        var normalized = FileSignaturesImageContentValidator.NormalizeExtension(extension);
        return normalized == ".jpeg" ? ".jpg" : normalized;
    }
}
