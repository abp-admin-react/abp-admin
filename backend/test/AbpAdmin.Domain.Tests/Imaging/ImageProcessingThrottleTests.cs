using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace AbpAdmin.Imaging;

/* T3.1 图片处理并发闸门：并发上限 + 等待超时转 503 业务异常。 */
public class ImageProcessingThrottleTests
{
    [Fact]
    public async Task Should_Execute_Action_And_Return_Result()
    {
        using var throttle = new ImageProcessingThrottle(Options.Create(new AbpAdminImagingOptions()));

        var result = await throttle.ExecuteAsync(_ => Task.FromResult(42));

        result.ShouldBe(42);
    }

    [Fact]
    public async Task Should_Throw_Busy_When_Semaphore_Is_Held_Beyond_ProcessTimeout()
    {
        using var throttle = new ImageProcessingThrottle(Options.Create(new AbpAdminImagingOptions
        {
            MaxDegreeOfParallelism = 1,
            ProcessTimeout = TimeSpan.FromMilliseconds(300)
        }));

        // 第一个调用占住唯一的闸门名额（action 不传 ct，确保不会被执行超时抢先释放信号量，
        // 否则执行超时总是比等待方的等待超时先到，这个用例就恒不成立）
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = throttle.ExecuteAsync(_ => gate.Task.ContinueWith(_ => 1));

        // 等它确实进入闸门后再发第二个调用
        await Task.Delay(100);

        var exception = await Should.ThrowAsync<AbpAdminImageProcessingBusyException>(
            () => throttle.ExecuteAsync(_ => Task.FromResult(2)));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Imaging.ImageProcessingBusy);
        exception.RetryAfterSeconds.ShouldBeGreaterThanOrEqualTo(1);
        exception.HttpStatusCode.ShouldBe(503);

        gate.SetResult();
        (await first).ShouldBe(1);
    }
}
