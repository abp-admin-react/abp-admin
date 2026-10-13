using System;
using System.IO;
using SkiaSharp;

namespace AbpAdmin.Imaging;

/// <summary>
/// 用 SKCodec 只读文件头解析图片尺寸与格式（不做完整解码），供解压炸弹的前置检查
/// 与「处理结果的实际格式/宽高」确认使用。
///
/// 「需先验证」结论（SkiaSharp 3.119 实测）：SKCodec.Create(Stream) 存在；
/// 对真实 PNG 只消耗文件头（实测读 4000x3000 PNG 后 Position 停在 57）即可给出
/// codec.Info.Width/Height 与 codec.EncodedFormat；文件头不完整时返回 null。
///
/// 有意识的取舍：这里直接使用了 SkiaSharp 类型（绕过 IImageResizer/IImageCompressor 抽象），
/// 仅限「读头部信息」这一个点；处理动作仍全部走 ABP 抽象，换 provider 时只需改这一个帮助类。
/// </summary>
public static class SkiaImageHeaderReader
{
    /// <summary>
    /// 尝试读取图片头部信息（始终从流的 0 位置读，因为图片头只在流开头）。
    /// 返回 false 表示无法解析（文件头不完整或不是可识别图片）。
    /// 方法返回时 Position 复位到调用前的值，流本身不会被关闭
    /// （SKCodec.Create(Stream) 会在 codec Dispose 时连带关闭流，所以这里走
    /// SKManagedStream(stream, disposeManagedStream: false) 包一层——已实测验证）。
    /// Extension 形如 ".jpg"（小写含点），无法映射时为 null。
    /// </summary>
    public static bool TryReadInfo(Stream stream, out int width, out int height, out string? extension)
    {
        var position = stream.CanSeek ? stream.Position : 0;
        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            using var skStream = new SKManagedStream(stream, disposeManagedStream: false);
            using var codec = SKCodec.Create(skStream);
            if (codec is null)
            {
                width = 0;
                height = 0;
                extension = null;
                return false;
            }

            width = codec.Info.Width;
            height = codec.Info.Height;
            extension = codec.EncodedFormat switch
            {
                SKEncodedImageFormat.Jpeg => ".jpg",
                SKEncodedImageFormat.Png => ".png",
                SKEncodedImageFormat.Webp => ".webp",
                SKEncodedImageFormat.Gif => ".gif",
                SKEncodedImageFormat.Bmp => ".bmp",
                _ => null
            };
            return width > 0 && height > 0;
        }
        catch (Exception)
        {
            width = 0;
            height = 0;
            extension = null;
            return false;
        }
        finally
        {
            if (stream.CanSeek)
            {
                stream.Position = position;
            }
        }
    }
}
