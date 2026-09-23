using System.IO;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.Imaging;

/* T3.1 LimitedCopyToAsync：带硬上限的流复制。
 * 关键点：超限立刻抛，不会先把整个流读完（客户端可以谎报 Content-Length），
 * 对应验收标准「服务端没有把超限内容全部读进内存」。
 */
public class LimitedStreamExtensionsTests
{
    [Fact]
    public async Task Should_Copy_Fully_When_Under_Limit()
    {
        var source = new MemoryStream(new byte[1000]);
        var destination = new MemoryStream();

        await source.LimitedCopyToAsync(destination, 2000);

        destination.Length.ShouldBe(1000);
    }

    [Fact]
    public async Task Should_Copy_Fully_When_Exactly_At_Limit()
    {
        var source = new MemoryStream(new byte[2000]);
        var destination = new MemoryStream();

        await source.LimitedCopyToAsync(destination, 2000);

        destination.Length.ShouldBe(2000);
    }

    [Fact]
    public async Task Should_Throw_Early_When_Over_Limit_Without_Reading_Whole_Stream()
    {
        var source = new MemoryStream(new byte[6 * 1024 * 1024]);
        var destination = new MemoryStream();
        var maxBytes = 5L * 1024 * 1024;

        var exception = await Should.ThrowAsync<BusinessException>(
            source.LimitedCopyToAsync(destination, maxBytes));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Imaging.AvatarTooLarge);
        // 提前中断：destination 不会超过上限，source 没有被读完
        destination.Length.ShouldBeLessThanOrEqualTo(maxBytes);
        source.Position.ShouldBeLessThan(source.Length);
    }
}
