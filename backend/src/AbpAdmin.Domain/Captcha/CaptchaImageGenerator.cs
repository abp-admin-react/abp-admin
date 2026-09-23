using System;
using SkiaSharp;

namespace AbpAdmin.Captcha;

/// <summary>
/// 图形验证码生成器（对标 RuoYi CaptchaController 的数学/字符验证码，取字符方案）。
/// 纯生成、无状态：4 位无歧义字符（去掉 0O1I 等）+ 干扰线 + 噪点，120x44 PNG。
/// SkiaSharp 与 T3.1 图片处理共用同一传递依赖（MIT），不新增包。
/// </summary>
public class CaptchaImageGenerator : Volo.Abp.Domain.Services.DomainService
{
    /// <summary>无歧义字符集：去掉 0/O、1/I/l、易混淆的 Z/2、S/5 等。</summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    private const int Width = 120;
    private const int Height = 44;
    private const int CharCount = 4;

    public virtual (string Code, byte[] Png) Generate()
    {
        var code = GenerateCode();
        return (code, Render(code));
    }

    protected virtual string GenerateCode()
    {
        var chars = new char[CharCount];
        for (var i = 0; i < CharCount; i++)
        {
            // 问题16 修复：改用密码学安全随机源（无偏、无负值）。
            // 原实现 Guid 字节取模：256 % 31 != 0 存在模偏差（熵略降，非安全漏洞）。
            chars[i] = Alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }

    protected virtual byte[] Render(string code)
    {
        using var bitmap = new SKBitmap(Width, Height);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(250, 250, 250));

        DrawNoiseLines(canvas);
        DrawCharacters(canvas, code);
        DrawNoisePixels(canvas);

        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 90);
        return png.ToArray();
    }

    private static void DrawCharacters(SKCanvas canvas, string code)
    {
        using var typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold);
        using var font = new SKFont(typeface, 26);

        var slotWidth = (float)Width / code.Length;
        for (var i = 0; i < code.Length; i++)
        {
            var x = slotWidth * i + slotWidth / 2f;
            var y = Height / 2f + NextFloat(-4, 4);

            canvas.Save();
            canvas.Translate(x, y);
            canvas.RotateDegrees(NextFloat(-14, 14));

            using var paint = new SKPaint
            {
                Color = new SKColor(
                    (byte)NextInt(20, 100),
                    (byte)NextInt(20, 100),
                    (byte)NextInt(20, 100)),
                IsAntialias = true
            };
            canvas.DrawText(code[i].ToString(), 0, 0, font, paint);
            canvas.Restore();
        }
    }

    private static void DrawNoiseLines(SKCanvas canvas)
    {
        for (var i = 0; i < 4; i++)
        {
            using var paint = new SKPaint
            {
                Color = new SKColor(
                    (byte)NextInt(120, 200),
                    (byte)NextInt(120, 200),
                    (byte)NextInt(120, 200)),
                StrokeWidth = 1.4f,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };
            canvas.DrawLine(
                NextFloat(0, Width), NextFloat(0, Height),
                NextFloat(0, Width), NextFloat(0, Height), paint);
        }
    }

    private static void DrawNoisePixels(SKCanvas canvas)
    {
        for (var i = 0; i < 80; i++)
        {
            using var paint = new SKPaint
            {
                Color = new SKColor(
                    (byte)NextInt(80, 220),
                    (byte)NextInt(80, 220),
                    (byte)NextInt(80, 220))
            };
            canvas.DrawRect(
                NextFloat(0, Width), NextFloat(0, Height), 1.5f, 1.5f, paint);
        }
    }

    private static int NextInt(int minInclusive, int maxExclusive)
    {
        // 问题16 修复：RandomNumberGenerator.GetInt32 无偏且恒为正区间值；
        // 原 Guid.GetHashCode() % range 可为负（颜色分量被 unchecked 截断）且有模偏差。
        return System.Security.Cryptography.RandomNumberGenerator.GetInt32(minInclusive, maxExclusive);
    }

    private static float NextFloat(float minInclusive, float maxExclusive)
    {
        return minInclusive + NextInt(0, 1_000_000) / 1_000_000f * (maxExclusive - minInclusive);
    }
}
