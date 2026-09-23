using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.ClickHouse;
using ClickHouse.Driver;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;
using AbpAdmin.Storage.Tests.TestInfrastructure;

namespace AbpAdmin.Storage.Tests.Storage;

/// <summary>
/// 真实 ClickHouse 集成测试(建表 DDL → 写入器入队/批量冲刷 → 回读断言)。
/// 环境变量 ABPADMIN_TEST_CLICKHOUSE_CONN 未配置时用例显示为 Skipped(见 RequiresEnvFactAttribute),
/// 不再以 Passed 伪装。
/// 注意:仓库无 CI workflow,本地/CI 跑真库需显式注入该环境变量,并可用
/// `--filter "Requires!=ClickHouse"` 排除。
/// </summary>
[Trait("Requires", "ClickHouse")]
public class ClickHouseWriterIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public ClickHouseWriterIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [RequiresEnvFact("ABPADMIN_TEST_CLICKHOUSE_CONN")]
    public async Task Writer_Should_Enqueue_Flush_And_Query_Back()
    {
        // 用独立测试表,不污染 collected_events 主表
        var options = new AbpAdminClickHouseOptions
        {
            IsEnabled = true,
            ConnectionString = Environment.GetEnvironmentVariable("ABPADMIN_TEST_CLICKHOUSE_CONN")!,
            CollectedEventsTable = "default.collected_events_it",
            MaxBatchSize = 100,
        };

        using var client = new ClickHouseClient(options.ConnectionString);
        await client.ExecuteNonQueryAsync($"DROP TABLE IF EXISTS {options.CollectedEventsTable}");
        try
        {
            // 建表移进 try:建表后的任何失败都能触发清理
            await client.ExecuteNonQueryAsync(CollectedEventsTable.BuildCreateTableSql(options.CollectedEventsTable));

            // 固定 Utc 毫秒时间:锁定 DateTime64(3) 写入路径的时区与毫秒精度
            var occurredAt = new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc);
            using var writer = new ClickHouseEventWriter(options, client, NullLogger<ClickHouseEventWriter>.Instance);
            writer.Enqueue(new CollectedEvent
            {
                TenantId = string.Empty,
                EventName = "it_test",
                Source = "integration-test",
                Payload = """{"n":1}""",
                OccurredAt = occurredAt,
            });
            writer.Enqueue(new CollectedEvent
            {
                TenantId = "3FA85F64-5717-4562-B3FC-2C963F66AFA6",
                EventName = "it_test",
                Source = "integration-test",
                Payload = """{"n":2}""",
                OccurredAt = occurredAt,
            });

            var written = await writer.FlushAsync(CancellationToken.None);
            Assert.Equal(2, written);

            // 重复冲刷应为空批(缓冲已清空),不产生重复数据
            Assert.Equal(0, await writer.FlushAsync(CancellationToken.None));

            var count = await client.ExecuteScalarAsync(
                $"SELECT count() FROM {options.CollectedEventsTable} WHERE event_name = 'it_test'");
            Assert.Equal(2L, Convert.ToInt64(count));

            // 时区/毫秒精度:写入的 Utc 时刻必须以毫秒精度落库(防 Kind 换算偏移)
            var exactTime = await client.ExecuteScalarAsync(
                $"SELECT count() FROM {options.CollectedEventsTable} " +
                "WHERE occurred_at = toDateTime64('2026-01-02 03:04:05.678', 3, 'UTC')");
            Assert.Equal(2L, Convert.ToInt64(exactTime));

            // ingested_at 必须被 CH 默认值填充(不允许 0 值)
            var zeroIngested = await client.ExecuteScalarAsync(
                $"SELECT count() FROM {options.CollectedEventsTable} WHERE ingested_at = toDateTime64(0, 3)");
            Assert.Equal(0L, Convert.ToInt64(zeroIngested));

            // 租户精确值回读(此前只断言 != '',截断/改写无法察觉)
            var tenantCount = await client.ExecuteScalarAsync(
                $"SELECT count() FROM {options.CollectedEventsTable} WHERE tenant_id = '3FA85F64-5717-4562-B3FC-2C963F66AFA6'");
            Assert.Equal(1L, Convert.ToInt64(tenantCount));
        }
        finally
        {
            // 清理自身失败不吞原始断言异常
            try
            {
                await client.ExecuteNonQueryAsync($"DROP TABLE IF EXISTS {options.CollectedEventsTable}");
            }
            catch (Exception dropEx)
            {
                _output.WriteLine($"清理测试表失败(忽略): {dropEx.Message}");
            }
        }
    }
}
