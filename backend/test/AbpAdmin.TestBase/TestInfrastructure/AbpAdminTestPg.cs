using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace AbpAdmin;

/// <summary>
/// 全仓测试共享的 Testcontainers PostgreSQL（默认镜像 postgres:16，与生产 / 运行开发样本同大版本；
/// 可用环境变量 ABPADMIN_TEST_PG_IMAGE 覆盖）。本机与 CI 跑后端测试都需要 Docker（见根 README「编译与测试」）。
/// <para>
/// 形态：每个测试进程一个容器（静态懒加载，进程退出后由 Testcontainers 的 Ryuk 守护回收，
/// 不留垃圾容器）；容器内每个测试程序集一个独立数据库——同进程多程序集（IDE 单进程跑多工程）
/// 与跨进程（dotnet test 每工程一个 testhost）都天然隔离，互不相扰。库名截断到 63 字节，
/// 截断后同名会显式抛错（共享库会互相清表，宁可红不可静默串库）。
/// </para>
/// <para>
/// 两种消费方式：① 模块集成测试（EFCore.Tests / Biz.Template.Tests 的测试模块）经
/// <see cref="GetDatabase"/> 拿程序集专属库连接串，对相应 DbContext 跑真迁移建表；
/// ② 真库门控用例（<see cref="RequiresHostDatabaseFactAttribute"/>）经
/// <see cref="TrySetHostOverrideEnvVar"/> 把容器连接串放进宿主覆盖环境变量，
/// 发现期门控与测试体取串同源，真库用例随进程容器走，不再依赖外部 PG。
/// </para>
/// </summary>
public static class AbpAdminTestPg
{
    private const string DefaultImage = "postgres:16";

    private static readonly Lazy<PostgreSqlContainer> Container =
        new(StartContainer, LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>程序集名 → 已建好的数据库句柄</summary>
    private static readonly ConcurrentDictionary<string, TestPgDatabase> Databases = new(StringComparer.Ordinal);

    /// <summary>库名 → 首个占用该库的程序集全名（截断碰撞守卫）</summary>
    private static readonly ConcurrentDictionary<string, string> DatabaseOwners = new(StringComparer.Ordinal);

    /// <summary>容器默认库（postgres:16 镜像下即镜像自举库）的连接串。</summary>
    public static string ContainerConnectionString => Container.Value.GetConnectionString();

    /// <summary>
    /// 把容器连接串放进宿主覆盖环境变量（<see cref="RequiresHostDatabaseFactAttribute.OverrideEnvVar"/>），
    /// 供真库门控用例发现与取串。已有显式环境变量时不动它（定向连某台 PG 调试的场景优先）；
    /// 容器起不来（无 Docker 等）静默返回——门控据此跳过真库用例，模块集成测试会在接线处以明确错误失败。
    /// </summary>
    public static void TrySetHostOverrideEnvVar()
    {
        var existing = Environment.GetEnvironmentVariable(RequiresHostDatabaseFactAttribute.OverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return;
        }

        try
        {
            Environment.SetEnvironmentVariable(
                RequiresHostDatabaseFactAttribute.OverrideEnvVar, ContainerConnectionString);
        }
        catch
        {
            // 容器不可用：留给 RequiresHostDatabaseFact 门控按「无覆盖」跳过
        }
    }

    /// <summary>
    /// 取程序集专属数据库（首次调用时在容器内建库）。测试模块在 ConfigureServices 里调：
    /// 拿连接串注入 AbpDbConnectionOptions，并用 <see cref="TestPgDatabase.MigrateOnce"/>
    /// 对相应 DbContext 跑真迁移建表（幂等，顺带验证各迁移链可空库自举）。
    /// </summary>
    public static TestPgDatabase GetDatabase(Assembly forAssembly)
    {
        var name = DatabaseName(forAssembly);
        var database = Databases.GetOrAdd(name, _ =>
        {
            Gate.Wait();
            try
            {
                CreateDatabase(name);
                return new TestPgDatabase(name, ConnectionStringTo(name));
            }
            finally
            {
                Gate.Release();
            }
        });

        // 截断碰撞守卫：两个程序集净化截断后同名 → 共享库互相清表，静默串库不可接受
        var owner = DatabaseOwners.GetOrAdd(name, _ => forAssembly.FullName ?? forAssembly.GetName().Name ?? "?");
        if (!string.Equals(owner, forAssembly.FullName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"测试库名冲突：程序集 {forAssembly.FullName} 与 {owner} 的净化名截断后同为 \"{name}\"。" +
                "共享库会让两个程序集的 Respawn 清表互相干扰——请缩短程序集名后重试。");
        }

        return database;
    }

    /// <summary>
    /// 用例间隔离：Respawn 清空该程序集库的全部业务表（保留迁移记账表）。
    /// 未注册库的程序集（纯单测，不碰数据库）零成本直通。放在测试基类 Dispose 里调用——
    /// 下一个用例启动时种子重新播，等价于此前「每用例一座独立内存库」的隔离语义。
    /// </summary>
    public static void Reset(Assembly forAssembly)
    {
        if (!Databases.TryGetValue(DatabaseName(forAssembly), out var database))
        {
            return;
        }

        database.Reset();
    }

    private static string DatabaseName(Assembly assembly)
    {
        // PG 库名上限 63 字节；abpadmin_ 前缀 + 程序集名（非字母数字折叠成 _、截断）足够区分且可读。
        // 截断碰撞由 GetDatabase 的守卫显式抛错兜底。
        var sanitized = (assembly.GetName().Name ?? "tests").ToLowerInvariant();
        sanitized = new string(sanitized.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        return ("abpadmin_" + sanitized)[..Math.Min(63, ("abpadmin_" + sanitized).Length)];
    }

    private static string ConnectionStringTo(string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(ContainerConnectionString)
        {
            Database = database,
        };
        return builder.ConnectionString;
    }

    private static void CreateDatabase(string database)
    {
        using var connection = new NpgsqlConnection(ContainerConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"CREATE DATABASE \"{database}\"";
        try
        {
            command.ExecuteNonQuery();
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.DuplicateDatabase)
        {
            // 同进程重复建库防御（正常不会走到；不同进程各有容器，不会撞名）
        }
    }

    private static PostgreSqlContainer StartContainer()
    {
        // 与框架 EF 模块同款 Npgsql 兼容开关：本类可能在任何模块接线之前建连接/建库，
        // 先钉住进程级开关，保证测试与运行时 timestamp 语义一致
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var image = Environment.GetEnvironmentVariable("ABPADMIN_TEST_PG_IMAGE");
        var container = new PostgreSqlBuilder()
            .WithImage(string.IsNullOrWhiteSpace(image) ? DefaultImage : image!)
            .Build();
        try
        {
            container.StartAsync().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                "启动测试用 PostgreSQL 容器失败——本仓后端测试依赖 Docker（Testcontainers）。" +
                "请确认 Docker 已安装且守护进程在运行；镜像默认 postgres:16，可用环境变量 " +
                "ABPADMIN_TEST_PG_IMAGE 覆盖。", e);
        }

        return container;
    }
}

/// <summary>一个测试程序集在共享容器里的专属数据库。</summary>
public sealed class TestPgDatabase
{
    private readonly ConcurrentDictionary<string, byte?> _migrated = new(StringComparer.Ordinal);
    private readonly object _migrateGate = new();
    private Lazy<Respawner> _respawner;

    internal TestPgDatabase(string name, string connectionString)
    {
        Name = name;
        ConnectionString = connectionString;
        _respawner = NewRespawnerLazy(connectionString);
    }

    public string Name { get; }

    public string ConnectionString { get; }

    /// <summary>
    /// 进程内一次性迁移守卫：同一库同一 key 只跑一次建表迁移（后续测试实例启动直接跳过，
    /// 迁移记账表保证幂等语义）。key 用 DbContext 名即可——守卫作用域本就限定在该库内。
    /// 迁移可能新建表（EFCore.Tests 里 Webhooks 上下文的表在基线上下文之后、首个
    /// Webhooks 用例启动时才建），Respawn 删除图必须随之作废重建——否则后建的表
    /// 永远不在清表清单里，用例间隔离对这些表静默失效。
    /// </summary>
    public void MigrateOnce(string key, Action migrate)
    {
        if (_migrated.ContainsKey(key))
        {
            return;
        }

        lock (_migrateGate)
        {
            if (_migrated.ContainsKey(key))
            {
                return;
            }

            migrate();
            _migrated[key] = default;
            // 删除图作废：下次 Reset 用当前 schema 重建（每库每进程至多重建数次，零常态成本）
            _respawner = NewRespawnerLazy(ConnectionString);
        }
    }

    /// <summary>Respawn 清空业务表（同步阻塞；调用点在测试 Dispose，无同步上下文死锁面）。</summary>
    public void Reset()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();
        _respawner.Value.ResetAsync(connection).GetAwaiter().GetResult();
    }

    private static Lazy<Respawner> NewRespawnerLazy(string connectionString) =>
        new(() =>
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            return Respawner.CreateAsync(connection, new RespawnerOptions
            {
                // 记账表自维护清单：从 information_schema 按 %EFMigrationsHistory 后缀现查——
                // 复制模块产生第四本账（__BizFoo_EFMigrationsHistory）时无需回来改这里；
                // 业务表以 App/Biz 前缀命名，不会误命中。空结果回落框架默认表名
                // （Reset 只在已迁移过的库上发生，正常不可能为空，防御性兜底）。
                TablesToIgnore = LoadHistoryTables(connection),
            }).GetAwaiter().GetResult();
        });

    private static Table[] LoadHistoryTables(NpgsqlConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema NOT IN ('pg_catalog', 'information_schema')
              AND table_name LIKE '%EFMigrationsHistory'
            ORDER BY table_name
            """;
        var names = new System.Collections.Generic.List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names.Count == 0
            ? new Table[] { "__EFMigrationsHistory" }
            : names.Select(n => new Table(n)).ToArray();
    }
}
