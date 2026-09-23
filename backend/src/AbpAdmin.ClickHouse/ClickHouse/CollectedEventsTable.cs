using System;
using System.Text.RegularExpressions;

namespace AbpAdmin.ClickHouse;

/// <summary>
/// collected_events 表定义。DDL、写入列序、行映射三者在此单头维护:
/// 加列时改本文件 + <see cref="CollectedEvent"/>,写入器(<see cref="ClickHouseEventWriter"/>)
/// 不感知具体列,避免三处人肉对齐造成列序错位。
/// </summary>
public static class CollectedEventsTable
{
    /// <summary>
    /// 写入列序,与 <see cref="ToRow"/> 返回的 object[] 位置一一对应,也与建表 DDL 的列顺序一致。
    /// ingested_at(落库时间)不在列内:由 CH 默认值 now64(3) 填充。
    /// </summary>
    public static readonly string[] Columns = { "tenant_id", "event_name", "source", "payload", "occurred_at" };

    /// <summary>
    /// 建表 DDL。MergeTree 按月分区(PARTITION BY toYYYYMM)、(event_name, occurred_at) 排序键;
    /// payload 用 ZSTD 压缩;occurred_at 为 DateTime64(3) 毫秒精度(按 CH server 时区解释,
    /// 写入值必须 Kind=Utc,见 <see cref="CollectedEvent.OccurredAt"/> 注释)。
    /// 表引擎是普通 MergeTree(不去重)——配合写入器的 at-least-once 语义,查询侧按需去重;
    /// 需要精确一次时改 ReplacingMergeTree + 幂等键,属表结构演进。
    /// </summary>
    /// <param name="tableName">目标表(库.表),先过 <see cref="EnsureValidTableName"/> 白名单。</param>
    /// <returns>幂等的 CREATE TABLE IF NOT EXISTS 语句,可安全重复执行(自愈路径依赖)。</returns>
    /// <exception cref="InvalidOperationException">表名未通过白名单校验。</exception>
    public static string BuildCreateTableSql(string tableName)
    {
        EnsureValidTableName(tableName);
        return $"""
                CREATE TABLE IF NOT EXISTS {tableName}
                (
                    tenant_id String,
                    event_name LowCardinality(String),
                    source LowCardinality(String),
                    payload String CODEC(ZSTD(3)),
                    occurred_at DateTime64(3),
                    ingested_at DateTime64(3) DEFAULT now64(3)
                )
                ENGINE = MergeTree
                PARTITION BY toYYYYMM(occurred_at)
                ORDER BY (event_name, occurred_at)
                """;
    }

    /// <summary>
    /// 表名来自配置(运维可改),凡拼接进 SQL 的路径(建表、以及未来任何按表名拼的语句)
    /// 都必须先过此白名单:\A...\z 全串锚定(默认 $ 会匹配结尾换行之前,"t\n" 能漏过),
    /// 仅放行 字母/数字/下划线 + 至多一个点分的 库.表 形态,阻断引号/空格/分号等注入载体。
    /// </summary>
    /// <param name="tableName">待校验表名。</param>
    /// <exception cref="InvalidOperationException">表名含白名单外字符或形态非法。</exception>
    public static void EnsureValidTableName(string tableName)
    {
        if (!Regex.IsMatch(tableName, @"\A[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)?\z"))
        {
            throw new InvalidOperationException($"非法的 ClickHouse 表名: {tableName}");
        }
    }

    /// <summary>
    /// 行映射:返回数组的位置序必须与 <see cref="Columns"/> 一致(InsertBinaryAsync 按位置对列)。
    /// </summary>
    public static object[] ToRow(CollectedEvent collectedEvent) =>
        new object[] { collectedEvent.TenantId, collectedEvent.EventName, collectedEvent.Source, collectedEvent.Payload, collectedEvent.OccurredAt };
}
