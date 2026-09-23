using System.IO;
using Shouldly;
using Xunit;

namespace AbpAdmin.Imaging;

/* T3.1 SKCodec 只读文件头取尺寸（规格要求用单测验证「喂已知尺寸 PNG，断言宽高正确」）。
 * 这是解码前像素上限（防解压炸弹）的可行性依据。
 */
public class SkiaImageHeaderReaderTests
{
    [Fact]
    public void Should_Read_Dimensions_From_Header_And_Restore_Position()
    {
        var bytes = TestImages.CreatePng(4000, 3000);
        using var stream = new MemoryStream(bytes);
        stream.Position = 5;

        SkiaImageHeaderReader.TryReadInfo(stream, out var width, out var height, out var extension)
            .ShouldBeTrue();

        width.ShouldBe(4000);
        height.ShouldBe(3000);
        extension.ShouldBe(".png");
        stream.Position.ShouldBe(5);
    }

    [Fact]
    public void Should_Return_False_For_Truncated_Data()
    {
        using var stream = new MemoryStream(new byte[] { 0x89, 0x50, 0x4E });

        SkiaImageHeaderReader.TryReadInfo(stream, out _, out _, out _).ShouldBeFalse();
    }
}
