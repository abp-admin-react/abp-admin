using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Shouldly;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// QuartzTablesEnsurer 的真库锚（无凭证自动跳过，CI 的 postgres 服务容器真跑）：
/// 幂等性（连跑两遍零异常）+ 建出的 11 张 qrtz_* 表能被 information_schema 查到——
/// 手工翻译的 DDL 与 Quartz StdAdoDelegate 的 performSchemaValidation 期望之间的
/// 漂移（列类型/缺表/缺外键）只在 Production 启动时爆炸，必须在测试期钉住。
/// </summary>
public class QuartzTablesEnsurerTests
{
    [RequiresHostDatabaseFact]
    public async Task EnsureCreated_Is_Idempotent_And_Creates_All_Quartz_Tables()
    {
        var connectionString = RequiresHostDatabaseFactAttribute.TryResolveOverride();
        connectionString.ShouldNotBeNull();

        await using var connection = new NpgsqlConnection(connectionString);
        await QuartzTablesEnsurer.EnsureCreatedAsync(connection, NullLogger.Instance);
        // 第二遍：IF NOT EXISTS 幂等——DbMigrator 重跑 / 宿主自迁移不得炸
        await QuartzTablesEnsurer.EnsureCreatedAsync(connection, NullLogger.Instance);

        await using var count = connection.CreateCommand();
        count.CommandText = """
            SELECT COUNT(*) FROM pg_tables
            WHERE schemaname = 'public' AND tablename LIKE 'qrtz_%'
            """;
        var tableCount = (long)(await count.ExecuteScalarAsync())!;

        // qrtz_job_details/triggers/simple_triggers/cron_triggers/simprop_triggers/blob_triggers/
        // calendars/paused_trigger_grps/fired_triggers/scheduler_state/locks
        tableCount.ShouldBe(11);
    }
}
