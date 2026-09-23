using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.ClickHouse;
using ClickHouse.Driver;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace AbpAdmin.Storage.Tests.ClickHouse;

/// <summary>
/// 写入器行为单测(NSubstitute fake 客户端,零库常绿)。
/// 对齐 <see cref="ClickHouseEventWriter"/> 类头注释承诺的五条行为:
/// at-least-once 失败退回、DropOldest 丢最旧、DDL 自愈(冷却)、停机预算(清空/耗尽)、
/// 门闩串行化(并发 FlushAsync 返回 0)、OCE 双分支语义。
/// </summary>
public class ClickHouseEventWriterTests
{
    private static AbpAdminClickHouseOptions Options(
        int bufferCapacity = 100,
        int maxBatchSize = 10,
        int flushIntervalMs = 50) =>
        new()
        {
            IsEnabled = true,
            ConnectionString = "Host=fake",
            CollectedEventsTable = "default.collected_events",
            BufferCapacity = bufferCapacity,
            MaxBatchSize = maxBatchSize,
            FlushInterval = TimeSpan.FromMilliseconds(flushIntervalMs),
        };

    private static CollectedEvent Event(string payload) => new()
    {
        EventName = "unit_test",
        Source = "tests",
        Payload = payload,
    };

    /// <summary>Payload 在行内的列位:经 Columns 反查,不复制 ToRow 的位置知识</summary>
    private static string PayloadOf(List<object[]> rows, int index) =>
        (string)rows[index][Array.IndexOf(CollectedEventsTable.Columns, "payload")];

    /// <summary>
    /// 常规 fake:前 <paramref name="failNextInserts"/> 次插入抛 HttpRequestException(可恢复),
    /// 之后正常记录行;DDL 自愈路径返回成功。
    /// 第 4/5 参数显式匹配(null + 任意令牌):写入器以命名参数传取消令牌,缺省匹配会不命中。
    /// </summary>
    private static IClickHouseClient CreateClient(List<object[]> writtenRows, int failNextInserts = 0)
    {
        var client = Substitute.For<IClickHouseClient>();
        client.ExecuteNonQueryAsync(Arg.Any<string>())
            .Returns(Task.FromResult(1));
        client.InsertBinaryAsync(
                Arg.Any<string>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<IEnumerable<object[]>>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (failNextInserts > 0)
                {
                    failNextInserts--;
                    throw new HttpRequestException("模拟 CH 不可用");
                }

                var rows = callInfo.Arg<IEnumerable<object[]>>().ToList();
                writtenRows.AddRange(rows);
                return Task.FromResult((long)rows.Count);
            });
        return client;
    }

    [Fact]
    public async Task FlushAsync_Should_Return_Zero_On_Empty_Buffer()
    {
        var written = new List<object[]>();
        using var writer = new ClickHouseEventWriter(Options(), CreateClient(written), NullLogger<ClickHouseEventWriter>.Instance);

        Assert.Equal(0, await writer.FlushAsync(CancellationToken.None));
        Assert.Empty(written);
    }

    [Fact]
    public async Task FlushAsync_Should_Requeue_And_Retry_After_Failure()
    {
        var written = new List<object[]>();
        var client = CreateClient(written, failNextInserts: 1);
        using var writer = new ClickHouseEventWriter(Options(), client, NullLogger<ClickHouseEventWriter>.Instance);
        writer.Enqueue(Event("a"));
        writer.Enqueue(Event("b"));

        // 第一轮失败:返回 0,事件退回缓冲
        Assert.Equal(0, await writer.FlushAsync(CancellationToken.None));
        Assert.Empty(written);

        // 第二轮恢复:同一批 2 行落库(失败退回的核心承诺)
        Assert.Equal(2, await writer.FlushAsync(CancellationToken.None));
        Assert.Equal(2, written.Count);
        Assert.Equal("a", PayloadOf(written, 0));
        Assert.Equal("b", PayloadOf(written, 1));
    }

    [Fact]
    public async Task FlushFailure_Should_Run_Ddl_SelfHeal_Once_Within_Cooldown()
    {
        var written = new List<object[]>();
        var client = CreateClient(written, failNextInserts: 2);
        using var writer = new ClickHouseEventWriter(Options(), client, NullLogger<ClickHouseEventWriter>.Instance);
        writer.Enqueue(Event("a"));

        // 第一次失败:触发一次自愈 DDL(幂等建表语句)
        Assert.Equal(0, await writer.FlushAsync(CancellationToken.None));
        await client.Received(1).ExecuteNonQueryAsync(
            Arg.Is<string>(s => s.Contains("CREATE TABLE IF NOT EXISTS")));

        // 冷却期(60s)内第二次失败:被冷却抑制,不再重复自愈
        Assert.Equal(0, await writer.FlushAsync(CancellationToken.None));
        await client.Received(1).ExecuteNonQueryAsync(Arg.Any<string>());

        // 恢复成功:成功路径永不触发自愈,总次数不变
        Assert.Equal(1, await writer.FlushAsync(CancellationToken.None));
        await client.Received(1).ExecuteNonQueryAsync(Arg.Any<string>());
        Assert.Single(written);
    }

    [Fact]
    public async Task Enqueue_BufferFull_Should_Drop_Oldest()
    {
        var written = new List<object[]>();
        using var writer = new ClickHouseEventWriter(Options(bufferCapacity: 2), CreateClient(written), NullLogger<ClickHouseEventWriter>.Instance);

        writer.Enqueue(Event("oldest"));
        writer.Enqueue(Event("middle"));
        writer.Enqueue(Event("newest")); // 挤掉 oldest

        Assert.Equal(2, await writer.FlushAsync(CancellationToken.None));
        Assert.Equal(2, written.Count);
        Assert.Equal("middle", PayloadOf(written, 0));
        Assert.Equal("newest", PayloadOf(written, 1));
    }

    [Fact]
    public async Task StopAsync_Should_Flush_Pending_Events()
    {
        var written = new List<object[]>();
        // FlushInterval 拉长:确保事件只能由停机冲刷落库
        var options = Options(flushIntervalMs: 10 * 60 * 1000);
        var logger = new ListLogger();
        using var writer = new ClickHouseEventWriter(options, CreateClient(written), logger);

        await writer.StartAsync(CancellationToken.None);
        writer.Enqueue(Event("pending"));
        // 不做任何间隔/等待:必须覆盖"Start→Enqueue→Stop 一气呵成"的竞态窗口——
        // .NET 10 BackgroundService 用 Task.Run(factory, token) 启动委托,取消先于
        // 线程池调度时委托不运行,ExecuteAsync 的 finally 冲刷整体被跳过(已修:StopAsync 兜底)
        await writer.StopAsync(CancellationToken.None);

        Assert.True(written.Count == 1,
            $"written={written.Count}, writerLogs=[{string.Join(" | ", logger.Entries)}]");
        Assert.Equal("pending", PayloadOf(written, 0));
    }

    /// <summary>捕获写入器日志的测试桩,失败时把日志倒进断言消息</summary>
    private sealed class ListLogger : ILogger<ClickHouseEventWriter>
    {
        public List<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add($"{logLevel}: {formatter(state, exception)}");
    }

    [Fact]
    public async Task StopAsync_Should_Drain_All_Batches_Until_Empty()
    {
        var written = new List<object[]>();
        var client = CreateClient(written);
        // 缓冲 30 条、单批 10 条:停机冲刷必须连续 3 批清空(清空为止的核心承诺)
        var options = Options(maxBatchSize: 10, flushIntervalMs: 10 * 60 * 1000);
        using var writer = new ClickHouseEventWriter(options, client, NullLogger<ClickHouseEventWriter>.Instance);

        await writer.StartAsync(CancellationToken.None);
        for (var i = 0; i < 30; i++)
        {
            writer.Enqueue(Event($"e{i}"));
        }

        await writer.StopAsync(CancellationToken.None);

        Assert.Equal(30, written.Count);
        await client.Received(3).InsertBinaryAsync(
            Arg.Any<string>(),
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<IEnumerable<object[]>>(),
            null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_Should_Abandon_When_Shutdown_Budget_Exhausted()
    {
        var written = new List<object[]>();
        var client = Substitute.For<IClickHouseClient>();
        client.ExecuteNonQueryAsync(Arg.Any<string>()).Returns(Task.FromResult(1));
        // 挂起的插入:真实消费取消令牌(模拟 CH 半开),预算一到即被打断
        client.InsertBinaryAsync(
                Arg.Any<string>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<IEnumerable<object[]>>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, callInfo.Arg<CancellationToken>());
                return 0L;
            });

        var options = Options(flushIntervalMs: 10 * 60 * 1000);
        using var writer = new ClickHouseEventWriter(
            options, client, NullLogger<ClickHouseEventWriter>.Instance,
            shutdownFlushBudget: TimeSpan.FromMilliseconds(300));

        await writer.StartAsync(CancellationToken.None);
        writer.Enqueue(Event("hang"));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        await writer.StopAsync(CancellationToken.None);
        stopwatch.Stop();

        // 预算生效:StopAsync 不被 2s 挂起拖死;未落库即放弃(残余告警走日志,此处验证行为)
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"StopAsync 耗时 {stopwatch.Elapsed},停机预算未生效");
        Assert.Empty(written);
    }

    [Fact]
    public async Task FlushAsync_Should_Throw_And_Requeue_When_Caller_Token_Cancelled_Mid_Insert()
    {
        var written = new List<object[]>();
        var client = Substitute.For<IClickHouseClient>();
        client.ExecuteNonQueryAsync(Arg.Any<string>()).Returns(Task.FromResult(1));
        // 只挂起第一次插入(消费写入器传入的调用方令牌),之后正常落库——
        // 否则取消后的二次冲刷会真的永久挂起
        var hangInserted = false;
        client.InsertBinaryAsync(
                Arg.Any<string>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<IEnumerable<object[]>>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(async callInfo =>
            {
                if (hangInserted)
                {
                    var rows = callInfo.Arg<IEnumerable<object[]>>().ToList();
                    written.AddRange(rows);
                    return (long)rows.Count;
                }

                hangInserted = true;
                await Task.Delay(Timeout.InfiniteTimeSpan, callInfo.Arg<CancellationToken>());
                return 0L;
            });
        using var writer = new ClickHouseEventWriter(Options(), client, NullLogger<ClickHouseEventWriter>.Instance);
        writer.Enqueue(Event("in-flight"));
        using var cts = new CancellationTokenSource();

        // FlushAsync 的同步段会走到挂起的插入并注册令牌;此刻取消=写入中途取消
        var flush = writer.FlushAsync(cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => flush);

        // 在途批已退回缓冲:再次冲刷应把同一事件落库(取消不丢数据)
        Assert.Equal(1, await writer.FlushAsync(CancellationToken.None));
        Assert.Single(written);
        Assert.Equal("in-flight", PayloadOf(written, 0));
    }

    [Fact]
    public async Task FlushAsync_Should_Treat_Unknown_Result_Oce_As_Failure()
    {
        var written = new List<object[]>();
        var client = Substitute.For<IClickHouseClient>();
        client.ExecuteNonQueryAsync(Arg.Any<string>()).Returns(Task.FromResult(1));
        // 仅第一次插入抛 OCE(结果未知的失败),之后正常落库——否则每次冲刷都失败无法收敛
        var oceThrown = false;
        // OCE 但调用方令牌未取消 = "结果未知"的失败:按失败退回处理,不上抛
        client.InsertBinaryAsync(
                Arg.Any<string>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<IEnumerable<object[]>>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                if (!oceThrown)
                {
                    oceThrown = true;
                    throw new OperationCanceledException("模拟超时但服务端可能已提交");
                }

                var rows = callInfo.Arg<IEnumerable<object[]>>().ToList();
                written.AddRange(rows);
                return Task.FromResult((long)rows.Count);
            });
        using var writer = new ClickHouseEventWriter(Options(), client, NullLogger<ClickHouseEventWriter>.Instance);
        writer.Enqueue(Event("maybe-committed"));

        // 第一次:OCE 未伴随调用方取消 → 吞成失败(0),事件退回
        Assert.Equal(0, await writer.FlushAsync(CancellationToken.None));

        // 第二次:同一事件重放落库(at-least-once,可能重复)
        Assert.Equal(1, await writer.FlushAsync(CancellationToken.None));
        Assert.Single(written);
    }

    [Fact]
    public async Task FlushAsync_Should_Return_Zero_When_Gate_Held_By_Another_Flush()
    {
        var written = new List<object[]>();
        var insertGate = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = Substitute.For<IClickHouseClient>();
        client.InsertBinaryAsync(
                Arg.Any<string>(),
                Arg.Any<IEnumerable<string>>(),
                Arg.Any<IEnumerable<object[]>>(),
                null,
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                written.AddRange(callInfo.Arg<IEnumerable<object[]>>());
                return insertGate.Task;
            });
        using var writer = new ClickHouseEventWriter(Options(), client, NullLogger<ClickHouseEventWriter>.Instance);
        writer.Enqueue(Event("held"));

        // 第一次冲刷:进入插入并挂起(行已记录、任务未完成)→ 门闩被持有
        var first = writer.FlushAsync(CancellationToken.None);
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (written.Count == 0 && wait.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(10);
        }

        Assert.NotEmpty(written);

        // 第二次冲刷:抢不到闩 → 立即返回 0,不重复消费同一批
        Assert.Equal(0, await writer.FlushAsync(CancellationToken.None));

        // 放行第一次冲刷:闩释放后缓冲已空,再冲为空批
        insertGate.TrySetResult(written.Count);
        Assert.Equal(1, await first);
        Assert.Equal(0, await writer.FlushAsync(CancellationToken.None));
    }
}
