using SkiaSharp;

namespace AbpAdmin.Imaging;

/// <summary>
/// 测试图片生成（T3.1）。用 SkiaSharp 编码出真实图片，
/// 避免在仓库里放二进制 fixture 文件。
/// </summary>
public static class TestImages
{
    /// <summary>最小 PE 文件头（MZ），用于「可执行文件改名成图片」的负例。</summary>
    public static byte[] ExeBytes { get; } =
    [
        0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00,
        0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00,
        0xB8, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x40, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    public static byte[] CreatePng(int width, int height)
    {
        return Encode(width, height, SKEncodedImageFormat.Png);
    }

    public static byte[] CreateJpeg(int width, int height)
    {
        return Encode(width, height, SKEncodedImageFormat.Jpeg);
    }

    private static byte[] Encode(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(200, 30, 30));
            // 画几个色块避免纯色图压缩率过高，失去「处理后字节数显著变小」的区分度
            using var paint = new SKPaint { Color = new SKColor(30, 30, 200) };
            canvas.DrawRect(width / 4, height / 4, width / 2, height / 2, paint);
            paint.Color = new SKColor(30, 200, 30);
            canvas.DrawCircle(width / 2, height / 2, width / 8f, paint);
        }

        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }
}
