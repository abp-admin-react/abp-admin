using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.Imaging;

/* T3.1 magic bytes 校验（FileSignatures 包装）正/负用例。
 * 对应验收标准：
 * - .exe 改名 .png 被拒；
 * - 真实 PNG 改名 .jpg 被拒（PNG 校验器不覆盖 .jpg，JPEG 校验器不认 PNG 头）；
 * - 白名单扩展名都能被库识别（启动期校验的判定方法本身）。
 */
public class FileSignaturesImageContentValidatorTests
{
    private static readonly string[] AllowedExtensions = [".jpg", ".jpeg", ".png"];

    private readonly FileSignaturesImageContentValidator _validator = new();

    [Fact]
    public async Task Should_Accept_Real_Png_And_Restore_Stream_Position()
    {
        var bytes = TestImages.CreatePng(64, 64);
        using var stream = new MemoryStream(bytes);
        stream.Position = 3;

        await _validator.ValidateAsync(stream, ".png", AllowedExtensions);

        stream.Position.ShouldBe(3);
    }

    [Theory]
    [InlineData(".jpg")]
    [InlineData(".jpeg")]
    [InlineData(".JPG")]
    public async Task Should_Accept_Real_Jpeg(string extension)
    {
        var bytes = TestImages.CreateJpeg(64, 64);
        using var stream = new MemoryStream(bytes);

        await _validator.ValidateAsync(stream, extension, AllowedExtensions);
    }

    [Fact]
    public async Task Should_Reject_Exe_Renamed_As_Png()
    {
        using var stream = new MemoryStream(TestImages.ExeBytes);

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _validator.ValidateAsync(stream, ".png", AllowedExtensions));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Imaging.InvalidImageContent);
    }

    [Fact]
    public async Task Should_Reject_Real_Png_Renamed_As_Jpg()
    {
        var bytes = TestImages.CreatePng(64, 64);
        using var stream = new MemoryStream(bytes);

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _validator.ValidateAsync(stream, ".jpg", AllowedExtensions));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Imaging.InvalidImageContent);
    }

    [Fact]
    public async Task Should_Reject_Extension_Outside_Whitelist()
    {
        var bytes = TestImages.CreatePng(64, 64);
        using var stream = new MemoryStream(bytes);

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _validator.ValidateAsync(stream, ".gif", AllowedExtensions));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Imaging.InvalidImageExtension);
    }

    [Fact]
    public async Task Should_Reject_Truncated_Header()
    {
        // 只有 3 个字节，不足以匹配任何签名
        using var stream = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E });

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _validator.ValidateAsync(stream, ".png", AllowedExtensions));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Imaging.InvalidImageContent);
    }

    [Fact]
    public void Startup_Check_Should_Pass_Default_Whitelist_And_Webp()
    {
        // 默认白名单 + .webp 都必须能被 FileSignatures 识别（双向约束）
        FileSignaturesImageContentValidator
            .GetUnrecognizableExtensions([".jpg", ".jpeg", ".png", ".webp"])
            .ShouldBeEmpty();
    }

    [Fact]
    public void Startup_Check_Should_Report_Unknown_Extension()
    {
        // 对应验收标准：白名单加入无法识别的扩展名 → 启动失败并指出缺失的扩展名
        var unrecognizable = FileSignaturesImageContentValidator
            .GetUnrecognizableExtensions([".jpg", ".zzz"]);

        unrecognizable.ShouldBe([".zzz"]);
    }
}
