using AbpAdmin.Webhooks;
using AbpAdmin.Webhooks.Data;
using Shouldly;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// WebhooksHistoryTableSql 的标识符转义契约（安全审计 LOW-1 修复的回归锚）：
/// schema 名拼进建表 DDL（标识符不能参数化），必须做 PG 双引号转义——
/// 含 `"` 的 schema 名若未转义，可提前终止标识符向 DDL 注入后续语句。
/// schema 来源是连接串 SearchPath（管理员配置面，可达性低），测试钉住的是
/// "无论输入形态如何转义恒成立"，不依赖对来源的信任层级。
/// </summary>
public class WebhooksHistoryTableSqlTests
{
    [Fact]
    public void Null_or_blank_schema_yields_unqualified_table()
    {
        var original = WebhooksDbSchemaMigrator.WebhooksHistorySchema;
        try
        {
            WebhooksDbSchemaMigrator.WebhooksHistorySchema = null;
            WebhooksDbSchemaMigrator.WebhooksHistoryTableSql
                .ShouldBe($"\"{AbpAdminWebhooksConsts.SchemaHistoryTable}\"");

            WebhooksDbSchemaMigrator.WebhooksHistorySchema = "  ";
            WebhooksDbSchemaMigrator.WebhooksHistoryTableSql
                .ShouldBe($"\"{AbpAdminWebhooksConsts.SchemaHistoryTable}\"");
        }
        finally
        {
            WebhooksDbSchemaMigrator.WebhooksHistorySchema = original;
        }
    }

    [Theory]
    [InlineData("app", "\"app\".\"__AbpAdminWebhooks_EFMigrationsHistory\"")]
    [InlineData("tenant_a", "\"tenant_a\".\"__AbpAdminWebhooks_EFMigrationsHistory\"")]
    // 注入形态：未转义的 `"` 会提前闭合标识符，把 schema 后半段变成任意 DDL 片段
    [InlineData("a\"--", "\"a\"\"--\".\"__AbpAdminWebhooks_EFMigrationsHistory\"")]
    [InlineData("x\"; DROP TABLE t;--", "\"x\"\"; DROP TABLE t;--\".\"__AbpAdminWebhooks_EFMigrationsHistory\"")]
    public void Schema_is_double_quote_escaped(string schema, string expected)
    {
        var original = WebhooksDbSchemaMigrator.WebhooksHistorySchema;
        try
        {
            WebhooksDbSchemaMigrator.WebhooksHistorySchema = schema;
            WebhooksDbSchemaMigrator.WebhooksHistoryTableSql.ShouldBe(expected);
        }
        finally
        {
            WebhooksDbSchemaMigrator.WebhooksHistorySchema = original;
        }
    }
}
