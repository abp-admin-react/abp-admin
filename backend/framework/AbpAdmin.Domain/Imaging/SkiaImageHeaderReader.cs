using System;
using System.IO;
using SkiaSharp;

namespace AbpAdmin.Imaging;

/// <summary>
/// 用 SKCodec 只读文件头解析图片尺寸与格式（不做完整解码），供解压炸弹的前置检查
/// 与「处理结果的实际格式/宽高」确认使用。
///
/// 实现方式（与 SafeSkiaImageContributors 同因同解）：把流头部拷进原生 SKData 后再建
/// codec，全程不创建 SKManagedStream。不用 SKManagedStream(disposeManagedStream:false)
/// 包流（Windows 上验证过）——但 native 侧对 wrapper 的句柄回收会延迟到 GC/finalizer
/// 阶段，那时调用方的流早已释放，Linux 上稳定触发「finalizer 线程读已关闭流」打崩进程
/// （CI 全量测试两轮复现；mono/SkiaSharp#2263 记载的 Stream 重载生命周期不可控问题）。
///
/// 有意识的取舍：这里直接使用了 SkiaSharp 类型（绕过 IImageResizer/IImageCompressor 抽象），
/// 仅限「读头部信息」这一个点；处理动作仍全部走 ABP 抽象，换 provider 时只需改这一个帮助类。
/// </summary>
public static class SkiaImageHeaderReader
{
    /// <summary>
    /// 头部探测预算：图片尺寸/格式信息都在头部——PNG IHDR 前 33 字节、WebP/BMP/GIF 几十字节、
    /// JPEG 的 SOF 在 EXIF 之后（真实相机 EXIF &lt; 64KB）。1 MiB 已是数千倍余量，且远小于
    /// 大文件上传的上限（全量拷不可接受）。取舍：头部超过预算仍解析不出尺寸的极端构造图片
    /// 会被判为「不可识别」而拒绝上传——安全方向收紧。
    /// </summary>
    private const int HeaderBudgetBytes = 1 << 20;

    /// <summary>
    /// 尝试读取图片头部信息（始终从流的 0 位置读，因为图片头只在流开头）。
    /// 返回 false 表示无法解析（文件头不完整或不是可识别图片）。
    /// 方法返回时 Position 复位到调用前的值，流本身不会被关闭。
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

            // 只拷头部进托管缓冲，再 SKData.Create 拷进原生内存（两次拷贝、上限 1 MiB，
            // 换取消灭全部 SKManagedStream——finalizer 崩溃的唯一残余来源）
            var buffer = new byte[HeaderBudgetBytes];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0)
                {
                    break;
                }

                read += n;
            }

            // 只拷头部进本地 MemoryStream，再 SKData.Create(Stream) 拷进原生内存——本地流在本
            // 方法内全程存活、离开即逝，调用方的流从不进 Skia（这是与旧 SKManagedStream 方案的
            // 本质区别：wrapper 的 native 句柄回收不再有机会触到外部流）
            using var header = new MemoryStream(buffer, 0, read, writable: false);
            using var data = SKData.Create(header);
            using var codec = SKCodec.Create(data);
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
