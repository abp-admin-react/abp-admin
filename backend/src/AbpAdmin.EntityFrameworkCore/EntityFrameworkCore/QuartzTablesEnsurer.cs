using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AbpAdmin.EntityFrameworkCore;

/// <summary>
/// Quartz 持久化表（qrtz_*，TablePrefix "QRTZ_" 在 PG 侧折叠为小写）的幂等建表。
/// 为什么不走 EF 迁移：EF 迁移面向 AbpAdminDbContext 的模型快照，qrtz_* 表不属于
/// 任何 DbContext 模型（Quartz 自管 schema），塞进模型迁移会在模型漂移时被误删改；
/// 幂等 DDL 挂在 <see cref="EntityFrameworkCoreAbpAdminDbSchemaMigrator"/> 第 ④ 步，
/// DbMigrator 与宿主自迁移（Database:AutoMigrateOnStartup）两条路径都会经过。
/// DDL 与 Quartz 官方 tables_postgres.sql 同构，全部 IF NOT EXISTS——重复执行零副作用，
/// Quartz 的 performSchemaValidation 直接放行。版本锚点：本 DDL 镜像自 ABP 10.6.1
/// （Directory.Packages.props 的 AbpVersion）传递引用的 Quartz 3.x 系列的
/// tables_postgres.sql——升级 ABP/Quartz 后若 store schema 有变，需对照上游脚本复核本文件；
/// 真库锚由 AbpAdmin.EntityFrameworkCore.Tests 的 QuartzTablesEnsurerTests 钉住
/// （幂等 ×2 + 11 表 information_schema 断言，无凭证自动跳过）。
/// 此前注释声称"QRTZ DDL 已随 EF 迁移"与事实不符（本地开发默认内存 store 从未踩到），
/// Production 姿势（UsePersistentStore=true）对全新库必崩，本类补上该缺口。
/// </summary>
internal static class QuartzTablesEnsurer
{
    /// <summary>表间有外键依赖，建表顺序不可随意调整；索引在全部表就位后建。</summary>
    internal const string Ddl = """
        CREATE TABLE IF NOT EXISTS qrtz_job_details
        (
            sched_name        VARCHAR(120) NOT NULL,
            job_name          VARCHAR(200) NOT NULL,
            job_group         VARCHAR(200) NOT NULL,
            description       VARCHAR(250) NULL,
            job_class_name    VARCHAR(250) NOT NULL,
            is_durable        BOOL         NOT NULL,
            is_nonconcurrent  BOOL         NOT NULL,
            is_update_data    BOOL         NOT NULL,
            requests_recovery BOOL         NOT NULL,
            job_data          BYTEA        NULL,
            PRIMARY KEY (sched_name, job_name, job_group)
        );

        CREATE TABLE IF NOT EXISTS qrtz_triggers
        (
            sched_name     VARCHAR(120) NOT NULL,
            trigger_name   VARCHAR(200) NOT NULL,
            trigger_group  VARCHAR(200) NOT NULL,
            job_name       VARCHAR(200) NOT NULL,
            job_group      VARCHAR(200) NOT NULL,
            description    VARCHAR(250) NULL,
            next_fire_time BIGINT       NULL,
            prev_fire_time BIGINT       NULL,
            priority       INTEGER      NULL,
            trigger_state  VARCHAR(16)  NOT NULL,
            trigger_type   VARCHAR(8)   NOT NULL,
            start_time     BIGINT       NOT NULL,
            end_time       BIGINT       NULL,
            calendar_name  VARCHAR(200) NULL,
            misfire_instr  SMALLINT     NULL,
            job_data       BYTEA        NULL,
            PRIMARY KEY (sched_name, trigger_name, trigger_group),
            FOREIGN KEY (sched_name, job_name, job_group)
                REFERENCES qrtz_job_details (sched_name, job_name, job_group)
        );

        CREATE TABLE IF NOT EXISTS qrtz_simple_triggers
        (
            sched_name      VARCHAR(120) NOT NULL,
            trigger_name    VARCHAR(200) NOT NULL,
            trigger_group   VARCHAR(200) NOT NULL,
            repeat_count    BIGINT       NOT NULL,
            repeat_interval BIGINT       NOT NULL,
            times_triggered BIGINT       NOT NULL,
            PRIMARY KEY (sched_name, trigger_name, trigger_group),
            FOREIGN KEY (sched_name, trigger_name, trigger_group)
                REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group)
        );

        CREATE TABLE IF NOT EXISTS qrtz_cron_triggers
        (
            sched_name      VARCHAR(120) NOT NULL,
            trigger_name    VARCHAR(200) NOT NULL,
            trigger_group   VARCHAR(200) NOT NULL,
            cron_expression VARCHAR(120) NOT NULL,
            time_zone_id    VARCHAR(80),
            PRIMARY KEY (sched_name, trigger_name, trigger_group),
            FOREIGN KEY (sched_name, trigger_name, trigger_group)
                REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group)
        );

        CREATE TABLE IF NOT EXISTS qrtz_simprop_triggers
        (
            sched_name    VARCHAR(120)  NOT NULL,
            trigger_name  VARCHAR(200)  NOT NULL,
            trigger_group VARCHAR(200)  NOT NULL,
            str_prop_1    VARCHAR(512)  NULL,
            str_prop_2    VARCHAR(512)  NULL,
            str_prop_3    VARCHAR(512)  NULL,
            int_prop_1    INT           NULL,
            int_prop_2    INT           NULL,
            long_prop_1   BIGINT        NULL,
            long_prop_2   BIGINT        NULL,
            dec_prop_1    NUMERIC(13,4) NULL,
            dec_prop_2    NUMERIC(13,4) NULL,
            bool_prop_1   BOOL          NULL,
            bool_prop_2   BOOL          NULL,
            PRIMARY KEY (sched_name, trigger_name, trigger_group),
            FOREIGN KEY (sched_name, trigger_name, trigger_group)
                REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group)
        );

        CREATE TABLE IF NOT EXISTS qrtz_blob_triggers
        (
            sched_name    VARCHAR(120) NOT NULL,
            trigger_name  VARCHAR(200) NOT NULL,
            trigger_group VARCHAR(200) NOT NULL,
            blob_data     BYTEA        NULL,
            PRIMARY KEY (sched_name, trigger_name, trigger_group),
            FOREIGN KEY (sched_name, trigger_name, trigger_group)
                REFERENCES qrtz_triggers (sched_name, trigger_name, trigger_group)
        );

        CREATE TABLE IF NOT EXISTS qrtz_calendars
        (
            sched_name    VARCHAR(120) NOT NULL,
            calendar_name VARCHAR(200) NOT NULL,
            calendar      BYTEA        NOT NULL,
            PRIMARY KEY (sched_name, calendar_name)
        );

        CREATE TABLE IF NOT EXISTS qrtz_paused_trigger_grps
        (
            sched_name    VARCHAR(120) NOT NULL,
            trigger_group VARCHAR(200) NOT NULL,
            PRIMARY KEY (sched_name, trigger_group)
        );

        CREATE TABLE IF NOT EXISTS qrtz_fired_triggers
        (
            sched_name        VARCHAR(120) NOT NULL,
            entry_id          VARCHAR(95)  NOT NULL,
            trigger_name      VARCHAR(200) NOT NULL,
            trigger_group     VARCHAR(200) NOT NULL,
            instance_name     VARCHAR(200) NOT NULL,
            fired_time        BIGINT       NOT NULL,
            sched_time        BIGINT       NOT NULL,
            priority          INTEGER      NOT NULL,
            state             VARCHAR(16)  NOT NULL,
            job_name          VARCHAR(200) NULL,
            job_group         VARCHAR(200) NULL,
            is_nonconcurrent  BOOL         NULL,
            requests_recovery BOOL         NULL,
            PRIMARY KEY (sched_name, entry_id)
        );

        CREATE TABLE IF NOT EXISTS qrtz_scheduler_state
        (
            sched_name        VARCHAR(120) NOT NULL,
            instance_name     VARCHAR(200) NOT NULL,
            last_checkin_time BIGINT       NOT NULL,
            checkin_interval  BIGINT       NOT NULL,
            PRIMARY KEY (sched_name, instance_name)
        );

        CREATE TABLE IF NOT EXISTS qrtz_locks
        (
            sched_name VARCHAR(120) NOT NULL,
            lock_name  VARCHAR(40)  NOT NULL,
            PRIMARY KEY (sched_name, lock_name)
        );

        CREATE INDEX IF NOT EXISTS idx_qrtz_j_req_recovery ON qrtz_job_details (sched_name, requests_recovery);
        CREATE INDEX IF NOT EXISTS idx_qrtz_j_grp ON qrtz_job_details (sched_name, job_group);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_j ON qrtz_triggers (sched_name, job_name, job_group);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_jg ON qrtz_triggers (sched_name, job_group);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_c ON qrtz_triggers (sched_name, calendar_name);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_g ON qrtz_triggers (sched_name, trigger_group);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_state ON qrtz_triggers (sched_name, trigger_state);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_n_state ON qrtz_triggers (sched_name, trigger_name, trigger_group, trigger_state);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_n_g_state ON qrtz_triggers (sched_name, trigger_group, trigger_state);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_next_fire_time ON qrtz_triggers (sched_name, next_fire_time);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_nft_st ON qrtz_triggers (sched_name, trigger_state, next_fire_time);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_nft_misfire ON qrtz_triggers (sched_name, misfire_instr, next_fire_time);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_nft_st_misfire ON qrtz_triggers (sched_name, misfire_instr, next_fire_time, trigger_state);
        CREATE INDEX IF NOT EXISTS idx_qrtz_t_nft_st_misfire_grp ON qrtz_triggers (sched_name, misfire_instr, next_fire_time, trigger_group, trigger_state);
        CREATE INDEX IF NOT EXISTS idx_qrtz_ft_trig_inst_name ON qrtz_fired_triggers (sched_name, instance_name);
        CREATE INDEX IF NOT EXISTS idx_qrtz_ft_inst_job_req_rcvry ON qrtz_fired_triggers (sched_name, instance_name, requests_recovery);
        CREATE INDEX IF NOT EXISTS idx_qrtz_ft_j_g ON qrtz_fired_triggers (sched_name, job_name, job_group);
        CREATE INDEX IF NOT EXISTS idx_qrtz_ft_jg ON qrtz_fired_triggers (sched_name, job_group);
        CREATE INDEX IF NOT EXISTS idx_qrtz_ft_t_g ON qrtz_fired_triggers (sched_name, trigger_name, trigger_group);
        CREATE INDEX IF NOT EXISTS idx_qrtz_ft_tg ON qrtz_fired_triggers (sched_name, trigger_group);
        """;

    /// <summary>幂等建表（连接由调用方管理，缺省时打开）。</summary>
    public static async Task EnsureCreatedAsync(NpgsqlConnection connection, ILogger logger)
    {
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var command = connection.CreateCommand();
        command.CommandText = Ddl;
        await command.ExecuteNonQueryAsync();

        logger.LogInformation("Quartz 持久化表（qrtz_*）已就绪（幂等 DDL，重复执行零副作用）。");
    }
}
