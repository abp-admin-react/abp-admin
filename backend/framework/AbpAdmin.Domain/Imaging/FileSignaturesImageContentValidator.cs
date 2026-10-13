using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FileSignatures;
using FileSignatures.Formats;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Imaging;

/// <summary>
/// 包装 FileSignatures 7.3.0（MIT）的 magic bytes 校验实现（T3.1）。
///
/// 「需先验证」结论（对 7.3.0 程序集反射 + 手工文件头实测）：
/// - 识别入口是 IFileFormatInspector.DetermineFileFormat(Stream)，
///   实例经 new FileFormatInspector(FileFormatLocator.GetFormats()) 构造；
///   规格提到的旧 API「FileFormatLocator.GetFileFormatLocator()」在 7.3.0 已不存在。
/// - JPEG 会被识别为子类 JpegJfif / JpegExif（IsAssignableFrom(Jpeg) 为 true），
///   所以匹配必须用基类型 IsInstanceOfType，不能用精确类型相等。
/// - FileFormat.Extension 不带点（"jpg"），MediaType 形如 "image/jpeg"。
/// - 流头残缺（字节数不足签名长度）时返回 null，按「无法识别 = 拒绝」处理。
/// </summary>
public class FileSignaturesImageContentValidator : IImageContentValidator, ITransientDependency
{
    /// <summary>
    /// 扩展名（小写含点）→ 期望的 FileSignatures 格式基类型。
    /// 这是「白名单扩展名 ↔ 库可识别格式」双向约束的载体；新增扩展名必须在这里有映射。
    /// </summary>
    private static readonly IReadOnlyDictionary<string, Type> ExpectedFormats = new Dictionary<string, Type>
    {
        [".jpg"] = typeof(Jpeg),
        [".jpeg"] = typeof(Jpeg),
        [".png"] = typeof(Png),
        [".webp"] = typeof(Webp),
        [".gif"] = typeof(Gif),
        [".bmp"] = typeof(Bmp),
    };

    private static readonly Lazy<IFileFormatInspector> Inspector =
        new(() => new FileFormatInspector(FileFormatLocator.GetFormats()));

    public virtual Task ValidateAsync(Stream stream, string extension, IReadOnlyList<string> allowedExtensions)
    {
        Check.NotNull(stream, nameof(stream));
        if (!stream.CanSeek)
        {
            throw new ArgumentException($"{nameof(stream)} 必须可 Seek。", nameof(stream));
        }

        var normalizedExtension = NormalizeExtension(extension);

        // 1. 扩展名白名单（双向约束的白名单侧）
        if (!allowedExtensions.Select(NormalizeExtension).Contains(normalizedExtension))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Imaging.InvalidImageExtension)
                .WithData("Extension", normalizedExtension);
        }

        // 2. 白名单通过但库不认识该扩展名 → 配置错误（启动期校验本该拦住），按扩展名非法拒绝
        if (!ExpectedFormats.TryGetValue(normalizedExtension, out var expectedFormatType))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Imaging.InvalidImageExtension)
                .WithData("Extension", normalizedExtension);
        }

        // 3. magic bytes：检测到的格式必须是期望类型或其子类（JPEG → JpegJfif/JpegExif）
        var position = stream.Position;
        try
        {
            var detected = Inspector.Value.DetermineFileFormat(stream);
            if (detected is null || !expectedFormatType.IsInstanceOfType(detected))
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Imaging.InvalidImageContent)
                    .WithData("Extension", normalizedExtension)
                    .WithData("DetectedMediaType", detected?.MediaType ?? "(unrecognized)");
            }
        }
        finally
        {
            stream.Position = position;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 启动期一致性校验：返回白名单中不能被库识别的扩展名（无映射，或映射的格式类型
    /// 不在 FileFormatLocator.GetFormats() 里）。空列表 = 全部可识别。
    /// AbpAdminDomainModule.OnApplicationInitialization 消费本方法，非空即抛 AbpInitializationException。
    /// </summary>
    public static IReadOnlyList<string> GetUnrecognizableExtensions(IEnumerable<string> extensions)
    {
        var knownFormats = FileFormatLocator.GetFormats().ToList();
        return extensions
            .Select(NormalizeExtension)
            .Where(ext =>
                !ExpectedFormats.TryGetValue(ext, out var expectedType) ||
                !knownFormats.Any(format => expectedType.IsInstanceOfType(format)))
            .ToList();
    }

    public static string NormalizeExtension(string extension)
    {
        var normalized = (extension ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.StartsWith('.') ? normalized : "." + normalized;
    }
}
