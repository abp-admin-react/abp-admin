using System;
using System.Collections.Generic;

namespace AbpAdmin.Imaging;

/// <summary>
/// T3.1 图片处理自研选项，绑定 appsettings.json 的 Imaging 节。
/// ABP provider 自身的选项（SkiaSharpCompressOptions / SkiaSharpResizerOptions / ImageResizeOptions）
/// 在 AbpAdminDomainModule 中单独配置，这里只放我们自己的约束。
/// </summary>
public class AbpAdminImagingOptions
{
    public const string SectionName = "Imaging";

    /// <summary>
    /// 头像允许的扩展名（小写，含点）。双向约束：每个扩展名必须能被
    /// FileSignaturesImageContentValidator 的格式映射识别，启动期校验不通过则 AbpInitializationException。
    /// 刻意不含 .svg（XXE / 内联脚本 / SSRF 攻击面，见规格 T3.1 第 9 步）。
    /// </summary>
    public List<string> AvatarAllowedExtensions { get; set; } = [".jpg", ".jpeg", ".png"];

    /// <summary>头像原始字节数上限（客户端可能谎报 Content-Length，读取时仍按此硬上限拦截）。</summary>
    public long AvatarMaxByteSize { get; set; } = 5 * 1024 * 1024;

    /// <summary>头像边长（正方形 Crop）。</summary>
    public uint AvatarSize { get; set; } = 256;

    public uint ThumbnailMaxWidth { get; set; } = 256;

    public uint ThumbnailMaxHeight { get; set; } = 256;

    /// <summary>图片处理是 CPU 密集操作，全进程并发上限。</summary>
    public int MaxDegreeOfParallelism { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);

    /// <summary>单次处理超时（含等待并发闸门的上限，等不到闸门返回 503）。</summary>
    public TimeSpan ProcessTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>解码前的像素上限，防解压炸弹。宽 * 高 超过此值直接拒绝。</summary>
    public long MaxPixelCount { get; set; } = 40_000_000;

    /// <summary>
    /// 会生成缩略图的扩展名。刻意不含 .svg。
    /// 注意集合大于 provider 能力：SkiaSharp contributor 只支持 jpeg/png/webp，
    /// gif/bmp 会落 Unsupported（跳过而非失败），这是预期行为。
    /// </summary>
    public List<string> ThumbnailSourceExtensions { get; set; } = [".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp"];
}
