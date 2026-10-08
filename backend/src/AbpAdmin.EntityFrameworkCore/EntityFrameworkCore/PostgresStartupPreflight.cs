using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Volo.Abp;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// PostgreSQL 启动预检：第一次运行没配置、配置了占位串、或 PG 连不上时，
/// 在迁移/框架初始化之前给出「一眼看懂、可行动」的致命错误并拒绝启动，
/// 而不是让开发者去读深层 Npgsql 堆栈。接线路径：
/// 框架迁移器 MigrateAsync 顶部（覆盖宿主自动迁移、DbMigrator、租户循环各自连接）
/// + 宿主模块迁移门控之前（AutoMigrateOnStartup=false 时也拦）。
/// 安全红线：任何错误信息只回显 Host/Port/Database/User，密码永不入日志与异常文本。
/// </summary>
public static class PostgresStartupPreflight
{
    /// <summary>出厂占位串标记（appsettings.json 兜底连接串的密码段）。</summary>
    public const string PlaceholderMarker = "CHANGE_ME";

    /// <summary>占位/未配置判定：空串，或包含占位标记（大小写不敏感）。</summary>
    public static bool IsUnconfiguredPlaceholder(string? connectionString)
        => string.IsNullOrWhiteSpace(connectionString)
           || connectionString.Contains(PlaceholderMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 确保连接串已配置且 PG 可达，否则 LogCritical + 抛 AbpInitializationException 拒绝启动。
    /// 探测连接强制 5 秒超时——首启失败快速暴露，不等默认 15 秒。
    /// </summary>
    public static async Task EnsureConnectableAsync(NpgsqlConnection connection, ILogger logger)
    {
        var connectionString = connection.ConnectionString;
        if (IsUnconfiguredPlaceholder(connectionString))
        {
            var notConfigured = """

                【启动中止】PostgreSQL 连接串未配置。
                当前 ConnectionStrings:Default 为空或仍是出厂占位串。本系统仅支持 PostgreSQL（单提供程序）。

                配置方式（二选一）：
                  1. 开发：编辑 backend/src/AbpAdmin.HttpApi.Host/appsettings.secrets.json
                     "ConnectionStrings": {
                       "Default": "Host=<主机>;Port=5432;Database=<库名>;Username=<用户>;Password=<密码>"
                     }
                  2. 生产/CI：环境变量 ConnectionStrings__Default（层级用双下划线）。
                配置后重启即自动建表/迁移。详见 docs/pg-migration-runbook.md。
                """;
            logger.LogCritical(notConfigured);
            throw new AbpInitializationException(notConfigured);
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString) { Timeout = 5 };
            await using var probe = new NpgsqlConnection(builder.ConnectionString);
            await probe.OpenAsync();
        }
        catch (PostgresException ex) when (ex.SqlState == "3D000")
        {
            // PG 服务器可达，但目标库不存在（全新安装/库名写错）。本系统不自动建库——
            // 建库是部署侧的一次性动作，给出明确建库指引而不是「连不上」。
            var shown = new NpgsqlConnectionStringBuilder(connectionString);
            var missing = $"""

                【启动中止】PostgreSQL 服务器可达，但目标库不存在。
                目标：Host={shown.Host}:{shown.Port}, Database={shown.Database}, User={shown.Username}（密码不回显）
                原因：{ex.MessageText}

                处理：先创建数据库再重启（任意 psql/管理工具执行）：
                  CREATE DATABASE "{shown.Database}";
                或把连接串指向已存在的库。详见 docs/pg-migration-runbook.md。
                """;
            logger.LogCritical(missing);
            throw new AbpInitializationException(missing, ex);
        }
        catch (Exception ex)
        {
            // 掩码展示串防御性构建：连接串本身非法（关键字拼错等）时 builder 会再抛，
            // 不能让原始 ArgumentException 从 catch 里逃逸、绕过整个可行动错误设计
            string displayed;
            try
            {
                var shown = new NpgsqlConnectionStringBuilder(connectionString);
                displayed = $"Host={shown.Host}:{shown.Port}, Database={shown.Database}, User={shown.Username}（密码不回显）";
            }
            catch
            {
                displayed = "（连接串无法解析，含非法关键字或格式错误——这本身就是要修的问题）";
            }

            var unreachable = $"""

                【启动中止】PostgreSQL 连不上。
                目标：{displayed}
                原因：{ex.GetBaseException().Message}

                排查：PG 服务是否启动/可达？pg_hba.conf 是否放行该用户与库？账号密码是否正确？
                连接串配置方式（二选一）：
                  1. 开发：backend/src/AbpAdmin.HttpApi.Host/appsettings.secrets.json 的 ConnectionStrings:Default
                  2. 生产/CI：环境变量 ConnectionStrings__Default。
                详见 docs/pg-migration-runbook.md。
                """;
            logger.LogCritical(unreachable);
            throw new AbpInitializationException(unreachable, ex);
        }
    }

    /// <summary>宿主模块便捷重载：从 IConfiguration 取 Default 连接串构造探测连接。</summary>
    public static Task EnsureConnectableAsync(IConfiguration configuration, ILogger logger)
    {
        var connection = new NpgsqlConnection(configuration.GetConnectionString("Default"));
        return EnsureConnectableAsync(connection, logger);
    }
}
