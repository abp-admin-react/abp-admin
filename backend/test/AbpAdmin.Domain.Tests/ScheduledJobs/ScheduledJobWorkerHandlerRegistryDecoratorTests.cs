using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.BackgroundWorkers;
using Xunit;

namespace AbpAdmin.ScheduledJobs;

/* 跨节点回退注册表回归测试（OCR：Quartz clustering 下 handler 委托缺失会被静默丢弃）。
 * 契约 pin：worker 名格式 "ScheduledJob:{id:N}" 是 decorator 与 scheduler 之间的
 * 字符串协议，这里用格式往返（N 格式解析、D 格式/畸形/异前缀拒绝）防止协议漂移。
 * DynamicBackgroundWorkerHandlerRegistry 与 decorator 均为纯逻辑，无需 ABP 测试基建。
 */
public class ScheduledJobWorkerHandlerRegistryDecoratorTests
{
    private readonly DynamicBackgroundWorkerHandlerRegistry _inner = new();
    private readonly ScheduledJobWorkerHandlerRegistryDecorator _decorator;

    public ScheduledJobWorkerHandlerRegistryDecoratorTests()
    {
        _decorator = new ScheduledJobWorkerHandlerRegistryDecorator(_inner);
    }

    private sealed class RecordingScheduler : IScheduledJobScheduler
    {
        public ConcurrentQueue<Guid> RunOnceCalls { get; } = new();

        public Task ScheduleAsync(ScheduledJob job, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task UnscheduleAsync(Guid scheduledJobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task TriggerAsync(Guid scheduledJobId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task RunOnceAsync(Guid scheduledJobId, IServiceProvider serviceProvider, CancellationToken cancellationToken)
        {
            RunOnceCalls.Enqueue(scheduledJobId);
            return Task.CompletedTask;
        }

        public Task ScheduleAllAsync(CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public void Get_Registered_Name_Should_Return_Inner_Handler()
    {
        static Task Inner(DynamicBackgroundWorkerExecutionContext context, CancellationToken ct) => Task.CompletedTask;
        _inner.Register("plain-worker", Inner);

        _decorator.Get("plain-worker").ShouldBe((DynamicBackgroundWorkerHandler)Inner);
    }

    [Fact]
    public void Get_ScheduledJob_N_Format_Should_Return_Fallback_Handler()
    {
        var handler = _decorator.Get($"ScheduledJob:{Guid.NewGuid():N}");

        handler.ShouldNotBeNull();
    }

    [Fact]
    public void Get_Non_N_Format_Should_Return_Null()
    {
        _decorator.Get($"ScheduledJob:{Guid.NewGuid():D}").ShouldBeNull();
    }

    [Fact]
    public void Get_Malformed_Suffix_Should_Return_Null()
    {
        _decorator.Get("ScheduledJob:notaguid").ShouldBeNull();
    }

    [Fact]
    public void Get_Foreign_Prefix_Should_Return_Null()
    {
        _decorator.Get($"Other:{Guid.NewGuid():N}").ShouldBeNull();
    }

    [Fact]
    public async Task Fallback_Handler_Should_Invoke_RunOnceAsync_With_Parsed_Id()
    {
        var jobId = Guid.NewGuid();
        var services = new ServiceCollection();
        var scheduler = new RecordingScheduler();
        services.AddSingleton<IScheduledJobScheduler>(scheduler);
        var provider = services.BuildServiceProvider();

        var handler = _decorator.Get($"ScheduledJob:{jobId:N}");
        handler.ShouldNotBeNull();

        await handler(
            new DynamicBackgroundWorkerExecutionContext($"ScheduledJob:{jobId:N}", provider),
            CancellationToken.None);

        scheduler.RunOnceCalls.ShouldContain(jobId);
    }
}
