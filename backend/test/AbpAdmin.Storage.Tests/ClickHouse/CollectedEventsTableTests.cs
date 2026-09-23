using System;
using AbpAdmin.ClickHouse;
using Xunit;

namespace AbpAdmin.Storage.Tests.ClickHouse;

/// <summary>
/// 表名白名单纯单测(零依赖,CI 常绿)。白名单是 BuildCreateTableSql 唯一的注入防线,
/// 合法/注入样例/边界(尾换行)必须被测试锁定。
/// </summary>
public class CollectedEventsTableTests
{
    [Theory]
    [InlineData("collected_events")]
    [InlineData("default.collected_events")]
    [InlineData("t")]
    public void BuildCreateTableSql_Should_Accept_ValidNames(string tableName)
    {
        var sql = CollectedEventsTable.BuildCreateTableSql(tableName);

        Assert.Contains($"CREATE TABLE IF NOT EXISTS {tableName}", sql);
    }

    [Theory]
    [InlineData("default.collected_events; DROP TABLE logs")] // 语句拼接
    [InlineData("`default`.`collected_events`")]              // 反引号
    [InlineData("default collected_events")]                  // 空格
    [InlineData("default-collected_events")]                  // 连字符(非库名分隔符)
    [InlineData("default.")]                                  // 空表名段
    [InlineData(".collected_events")]                         // 空库名段
    [InlineData("")]                                          // 空串
    [InlineData("collected_events\n")]                        // 尾换行:$ 锚会漏放,\A\z 必须拦住
    [InlineData("collected_events ")]                         // 尾空格
    public void BuildCreateTableSql_Should_Reject_InvalidNames(string tableName)
    {
        Assert.Throws<InvalidOperationException>(() => CollectedEventsTable.BuildCreateTableSql(tableName));
    }

    [Fact]
    public void EnsureValidTableName_Should_Throw_On_Null()
    {
        Assert.ThrowsAny<Exception>(() => CollectedEventsTable.EnsureValidTableName(null!));
    }

    [Fact]
    public void ToRow_Should_Match_Columns_Order()
    {
        var e = new CollectedEvent
        {
            TenantId = "tenant-1",
            EventName = "evt",
            Source = "src",
            Payload = """{"n":1}""",
        };

        var row = CollectedEventsTable.ToRow(e);

        Assert.Equal(CollectedEventsTable.Columns.Length, row.Length);
        Assert.Equal("tenant-1", row[0]);
        Assert.Equal("evt", row[1]);
        Assert.Equal("src", row[2]);
        Assert.Equal("""{"n":1}""", row[3]);
        Assert.Equal(e.OccurredAt, row[4]);
    }
}
