using System;
using Microsoft.Extensions.Configuration;

namespace AbpAdmin.Webhooks.EntityFrameworkCore;

/// <summary>
/// 迁移 History 表 schema 的唯一出处：从连接串 <c>SearchPath</c> 解析（缺省 public）。
/// 背景：Npgsql 对 <c>MigrationsHistoryTable(name)</code>（schema 省略）的 applied-migrations
/// 查询未按连接串 search_path 解析（框架 __EFMigrationsHistory 未定制则正常），
/// 显式传 schema 才能让 DbMigrator 写入的记账行被宿主启动读回。
/// </summary>
public static class WebhooksHistorySchemaResolver
{
    public const string DefaultSchema = "public";

    public static (string Schema, string BaseConnectionString) Resolve(string connectionString)
    {
        var schema = DefaultSchema;
        var baseConn = connectionString ?? string.Empty;

        var keyIndex = baseConn.IndexOf("SearchPath=", StringComparison.OrdinalIgnoreCase);
        if (keyIndex >= 0)
        {
            var valueStart = keyIndex + "SearchPath=".Length;
            var end = baseConn.IndexOf(';', valueStart);
            var raw = end < 0 ? baseConn[valueStart..] : baseConn[valueStart..end];
            if (!string.IsNullOrWhiteSpace(raw))
            {
                schema = raw.Split(',')[0].Trim();
            }

            // 从连接串移除 SearchPath（schema 已显式传给 History 表，连接级 search_path 留给数据访问）
            baseConn = (baseConn[..keyIndex] + (end < 0 ? string.Empty : baseConn[end..])).TrimEnd(';');
        }

        return (schema, baseConn);
    }
}
