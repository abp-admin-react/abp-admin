using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Npgsql;

namespace AbpAdmin;

/// <summary>
/// 测试进程内共享的“完整初始化宿主”（自 <see cref="Authorization.AnonymousEndpointSweepTests"/>
/// 的 LazyHost 提升而来的公共基建——该类注释原本就要求第二个需要完整宿主的测试类
/// 复用而不是再复制一份）。宿主初始化到端点表构建完成（InitializeApplicationAsync），
/// 不启动 Kestrel；写副作用落在 Testcontainers PG 的专用 schema（abp_admin_hosttest）里。
/// 宿主进程内静态共享且从不 Dispose：Quartz 的 LogProvider 把 LoggerFactory 缓存在静态
/// 字段里，Dispose 后再初始化会撞 ObjectDisposedException（AnonymousEndpointSweepTests
/// 2026-09 实证）——测试进程结束即回收，泄漏是刻意的、可接受的。
/// </summary>
internal static class HostTestBed
{
    private static readonly Lazy<Task<WebApplication>> LazyHost = new(CreateInitializedHostAsync);

    public static Task<WebApplication> HostTask => LazyHost.Value;

    private static async Task<WebApplication> CreateInitializedHostAsync()
    {
        // 宿主是 PostgreSQL-only（EF 模块无条件 UseNpgsql）。凭证取真实覆盖层
        // （环境变量 ConnectionStrings__Default → appsettings.secrets.json；tracked 基座
        // 占位串不算配置，见 RequiresHostDatabaseFactAttribute；本程序集加载时通常已由
        // TestPgAssemblyInitializer 注入容器连接串），schema 隔离到专用 abp_admin_hosttest
        // （不存在则创建；宿主启动的 AutoMigrateOnStartup 会在其中自举迁移+种子），
        // 与 dev schema（abp_admin_efm）互不干扰。
        var baseConn = RequiresHostDatabaseFactAttribute.TryResolveOverride()
                       ?? throw new InvalidOperationException(
                           "完整宿主测试需要 PostgreSQL 连接串（宿主已 PG-only）。" +
                           "请在 framework/AbpAdmin.HttpApi.Host/appsettings.secrets.json 配置 ConnectionStrings:Default（含凭据）。");
        var connString = await EnsureIsolatedSchemaAsync(baseConn, "abp_admin_hosttest");

        var builder = await HostUnderTest.CreateBuilderAsync(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connString,
            },
            useAutofac: true);
        var app = builder.Build();
        await app.InitializeApplicationAsync();
        return app;
    }

    /// <summary>确保专用 schema 存在并返回追加 SearchPath 的连接串（测试写副作用全部落在该 schema）。</summary>
    private static async Task<string> EnsureIsolatedSchemaAsync(string connectionString, string schemaName)
    {
        var bare = connectionString.Contains("SearchPath=", StringComparison.OrdinalIgnoreCase)
            ? connectionString[..connectionString.IndexOf("SearchPath=", StringComparison.OrdinalIgnoreCase)].TrimEnd(';')
            : connectionString;

        await using var conn = new NpgsqlConnection(bare);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            $"CREATE SCHEMA IF NOT EXISTS {schemaName}", conn);
        await cmd.ExecuteNonQueryAsync();

        return bare.EndsWith(";") ? bare + $"SearchPath={schemaName}" : bare + $";SearchPath={schemaName}";
    }
}
