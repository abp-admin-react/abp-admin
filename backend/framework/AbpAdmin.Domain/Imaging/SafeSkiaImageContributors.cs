using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Http;
using Volo.Abp.Imaging;

namespace AbpAdmin.Imaging;

/// <summary>
/// 安全版 SkiaSharp 缩放/压缩贡献者：语义与 ABP 10.6.1 的 SkiaSharpImageResizerContributor /
/// SkiaSharpImageCompressorContributor 一致（mime 门控、六种缩放模式、压缩不变小则 Canceled、
/// 同一份 SkiaSharpResizerOptions/SkiaSharpCompressOptions 配置），但解码走
/// <c>SKData.Create(byte[])</c>——字节先拷进原生内存，全程不创建 SKManagedStream。
/// 由 ImagingConfigurator 以 Replace 注册替换 ABP 原 contributor。
///
/// 为什么必须替换：ABP 原实现用 <c>SKCodec.Create(Stream)</c>/<c>SKBitmap.Decode(Stream)</c>，
/// 这两个重载内部创建的 SKManagedStream 是 GC 孤儿（无人显式 Dispose），而外层 finally 先把
/// 托管 MemoryStream Dispose 掉——孤儿 SKManagedStream 被 finalizer 回收时，原生析构会经
/// 读代理回调托管流，命中已关闭的流 → finalizer 线程 ObjectDisposedException →
/// 直接打崩整个进程（无人捕获）。Windows 上 GC 时序碰不到，Linux CI 全量测试稳定触发；
/// 生产容器（Linux）头像上传同样暴露在此风险下。
/// </summary>
public class SafeSkiaSharpImageResizerContributor : IImageResizerContributor, ITransientDependency
{
    protected SkiaSharpResizerOptions Options { get; }

    public SafeSkiaSharpImageResizerContributor(IOptions<SkiaSharpResizerOptions> options)
    {
        Options = options.Value;
    }

    public virtual async Task<ImageResizeResult<byte[]>> TryResizeAsync(
        byte[] bytes, ImageResizeArgs resizeArgs, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        if (!mimeType.IsNullOrWhiteSpace() && !CanResize(mimeType))
        {
            return new ImageResizeResult<byte[]>(bytes, ImageProcessState.Unsupported);
        }

        // 与 ABP 原实现同构：byte[] 重载经 MemoryStream 走流版本（我们的流版本本身安全）
        using var memoryStream = new MemoryStream(bytes);
        var result = await TryResizeAsync(memoryStream, resizeArgs, mimeType, cancellationToken);

        if (result.State != ImageProcessState.Done)
        {
            return new ImageResizeResult<byte[]>(bytes, result.State);
        }

        var newBytes = await result.Result.GetAllBytesAsync(cancellationToken);
        result.Result.Dispose();

        return new ImageResizeResult<byte[]>(newBytes, result.State);
    }

    public virtual async Task<ImageResizeResult<Stream>> TryResizeAsync(
        Stream stream, ImageResizeArgs resizeArgs, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        if (!mimeType.IsNullOrWhiteSpace() && !CanResize(mimeType))
        {
            return new ImageResizeResult<Stream>(stream, ImageProcessState.Unsupported);
        }

        // 拷进原生内存后解码：codec/bitmap 的整个生命周期只引用 SKData，无任何托管流代理
        using var data = await LoadSkDataAsync(stream, cancellationToken);
        using var codec = SKCodec.Create(data);
        if (codec is null || !CanEncodeFormat(codec.EncodedFormat))
        {
            return new ImageResizeResult<Stream>(stream, ImageProcessState.Unsupported);
        }

        using var original = SKBitmap.Decode(data);
        if (original is null)
        {
            return new ImageResizeResult<Stream>(stream, ImageProcessState.Unsupported);
        }

        using var resized = ApplyResize(original, resizeArgs);
        using var image = SKImage.FromBitmap(resized);

        var output = new MemoryStream();
        try
        {
            using var encoded = image.Encode(codec.EncodedFormat, Options.Quality);
            encoded.SaveTo(output);
            output.Position = 0;
            return new ImageResizeResult<Stream>(output, ImageProcessState.Done);
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    /// <summary>把流完整读入原生 SKData（调用方 using 负责 Dispose）。保持 ABP 语义：可 Seek 的流复位原 Position。</summary>
    internal static async Task<SKData> LoadSkDataAsync(Stream stream, CancellationToken cancellationToken)
    {
        var position = stream.CanSeek ? stream.Position : 0;
        try
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            // SKData.Create(Stream) 把内容拷进原生内存后即与托管流断开——不产生引用
            // 托管流的 SKManagedStream 孤儿（ABP 原 contributor 的崩溃根源）
            return SKData.Create(buffer);
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = position;
            }
        }
    }

    protected virtual bool CanResize(string? mimeType)
    {
        return mimeType switch
        {
            MimeTypes.Image.Jpeg => true,
            MimeTypes.Image.Png => true,
            MimeTypes.Image.Webp => true,
            _ => false,
        };
    }

    protected virtual bool CanEncodeFormat(SKEncodedImageFormat format)
    {
        return format switch
        {
            SKEncodedImageFormat.Jpeg => true,
            SKEncodedImageFormat.Png => true,
            SKEncodedImageFormat.Webp => true,
            _ => false,
        };
    }

    /// <summary>六种缩放模式的目标框计算与绘制——逐模式镜像 ABP 原实现（Stretch/Max/Min/Crop/Pad/BoxPad）。</summary>
    protected virtual SKBitmap ApplyResize(SKBitmap source, ImageResizeArgs resizeArgs)
    {
        var targetWidth = (int)resizeArgs.Width;
        var targetHeight = (int)resizeArgs.Height;

        if (targetWidth <= 0 && targetHeight <= 0)
        {
            return source.Copy();
        }

        if (targetWidth <= 0)
        {
            targetWidth = Math.Max(1, (int)Math.Round((double)source.Width * targetHeight / source.Height));
        }
        else if (targetHeight <= 0)
        {
            targetHeight = Math.Max(1, (int)Math.Round((double)source.Height * targetWidth / source.Width));
        }

        var mode = resizeArgs.Mode == ImageResizeMode.Default ? ImageResizeMode.Stretch : resizeArgs.Mode;

        switch (mode)
        {
            case ImageResizeMode.None:
            case ImageResizeMode.Stretch:
                return source.Resize(new SKImageInfo(targetWidth, targetHeight), Options.SKSamplingOptions);

            case ImageResizeMode.Max:
            {
                var scale = Math.Min((double)targetWidth / source.Width, (double)targetHeight / source.Height);
                return source.Resize(
                    new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale))),
                    Options.SKSamplingOptions);
            }

            case ImageResizeMode.Min:
            {
                var scale = Math.Max((double)targetWidth / source.Width, (double)targetHeight / source.Height);
                return source.Resize(
                    new SKImageInfo(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale))),
                    Options.SKSamplingOptions);
            }

            case ImageResizeMode.Crop:
            {
                var scale = Math.Max((double)targetWidth / source.Width, (double)targetHeight / source.Height);
                using var intermediate = source.Resize(
                    new SKImageInfo(
                        Math.Max(1, (int)Math.Round(source.Width * scale)),
                        Math.Max(1, (int)Math.Round(source.Height * scale))),
                    Options.SKSamplingOptions);

                var bitmap = new SKBitmap(targetWidth, targetHeight);
                try
                {
                    using var canvas = new SKCanvas(bitmap);
                    var srcX = (intermediate.Width - targetWidth) / 2;
                    var srcY = (intermediate.Height - targetHeight) / 2;
                    canvas.DrawBitmap(
                        intermediate,
                        new SKRect(srcX, srcY, srcX + targetWidth, srcY + targetHeight),
                        new SKRect(0, 0, targetWidth, targetHeight));
                    return bitmap;
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
            }

            case ImageResizeMode.Pad:
            {
                var scale = Math.Min((double)targetWidth / source.Width, (double)targetHeight / source.Height);
                using var intermediate = source.Resize(
                    new SKImageInfo(
                        Math.Max(1, (int)Math.Round(source.Width * scale)),
                        Math.Max(1, (int)Math.Round(source.Height * scale))),
                    Options.SKSamplingOptions);

                var bitmap = new SKBitmap(targetWidth, targetHeight);
                try
                {
                    using var canvas = new SKCanvas(bitmap);
                    canvas.Clear(SKColors.Transparent);
                    canvas.DrawBitmap(intermediate, new SKPoint((targetWidth - intermediate.Width) / 2, (targetHeight - intermediate.Height) / 2));
                    return bitmap;
                }
                catch
                {
                    bitmap.Dispose();
                    throw;
                }
            }

            case ImageResizeMode.BoxPad:
            {
                SKBitmap? scaled = null;
                try
                {
                    var working = source;
                    if (source.Width > targetWidth || source.Height > targetHeight)
                    {
                        var scale = Math.Min((double)targetWidth / source.Width, (double)targetHeight / source.Height);
                        scaled = source.Resize(
                            new SKImageInfo(
                                Math.Max(1, (int)Math.Round(source.Width * scale)),
                                Math.Max(1, (int)Math.Round(source.Height * scale))),
                            Options.SKSamplingOptions);
                        working = scaled;
                    }

                    var bitmap = new SKBitmap(targetWidth, targetHeight);
                    try
                    {
                        using var canvas = new SKCanvas(bitmap);
                        canvas.Clear(SKColors.Transparent);
                        canvas.DrawBitmap(working, new SKPoint((targetWidth - working.Width) / 2, (targetHeight - working.Height) / 2));
                        return bitmap;
                    }
                    catch
                    {
                        bitmap.Dispose();
                        throw;
                    }
                }
                finally
                {
                    scaled?.Dispose();
                }
            }

            default:
                throw new NotSupportedException("Resize mode " + resizeArgs.Mode + " is not supported!");
        }
    }
}

/// <summary>
/// 安全版 SkiaSharp 压缩贡献者（与 <see cref="SafeSkiaSharpImageResizerContributor"/> 同理由替换
/// ABP 原实现；语义镜像：重编码后不变小则返回 Canceled 且原样返回输入流）。
/// </summary>
public class SafeSkiaSharpImageCompressorContributor : IImageCompressorContributor, ITransientDependency
{
    protected SkiaSharpCompressOptions Options { get; }

    public SafeSkiaSharpImageCompressorContributor(IOptions<SkiaSharpCompressOptions> options)
    {
        Options = options.Value;
    }

    public virtual async Task<ImageCompressResult<Stream>> TryCompressAsync(
        Stream stream, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        if (!mimeType.IsNullOrWhiteSpace() && !CanCompress(mimeType))
        {
            return new ImageCompressResult<Stream>(stream, ImageProcessState.Unsupported);
        }

        var originalLength = stream.CanSeek ? stream.Length : 0;
        using var data = await SafeSkiaSharpImageResizerContributor.LoadSkDataAsync(stream, cancellationToken);
        using var codec = SKCodec.Create(data);
        if (codec is null || !CanEncodeFormat(codec.EncodedFormat))
        {
            return new ImageCompressResult<Stream>(stream, ImageProcessState.Unsupported);
        }

        using var bitmap = SKBitmap.Decode(data);
        if (bitmap is null)
        {
            return new ImageCompressResult<Stream>(stream, ImageProcessState.Unsupported);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(codec.EncodedFormat, Options.Quality);

        var output = new MemoryStream();
        try
        {
            encoded.SaveTo(output);
            output.Position = 0;

            if (output.Length < originalLength)
            {
                return new ImageCompressResult<Stream>(output, ImageProcessState.Done);
            }

            output.Dispose();
            return new ImageCompressResult<Stream>(stream, ImageProcessState.Canceled);
        }
        catch
        {
            output.Dispose();
            throw;
        }
    }

    public virtual async Task<ImageCompressResult<byte[]>> TryCompressAsync(
        byte[] bytes, string? mimeType = null, CancellationToken cancellationToken = default)
    {
        if (!mimeType.IsNullOrWhiteSpace() && !CanCompress(mimeType))
        {
            return new ImageCompressResult<byte[]>(bytes, ImageProcessState.Unsupported);
        }

        using var memoryStream = new MemoryStream(bytes);
        var result = await TryCompressAsync(memoryStream, mimeType, cancellationToken);

        if (result.State != ImageProcessState.Done)
        {
            return new ImageCompressResult<byte[]>(bytes, result.State);
        }

        var newBytes = await result.Result.GetAllBytesAsync(cancellationToken);
        result.Result.Dispose();

        return new ImageCompressResult<byte[]>(newBytes, result.State);
    }

    protected virtual bool CanCompress(string? mimeType)
    {
        return mimeType switch
        {
            MimeTypes.Image.Jpeg => true,
            MimeTypes.Image.Png => true,
            MimeTypes.Image.Webp => true,
            _ => false,
        };
    }

    protected virtual bool CanEncodeFormat(SKEncodedImageFormat format)
    {
        return format switch
        {
            SKEncodedImageFormat.Jpeg => true,
            SKEncodedImageFormat.Png => true,
            SKEncodedImageFormat.Webp => true,
            _ => false,
        };
    }
}
