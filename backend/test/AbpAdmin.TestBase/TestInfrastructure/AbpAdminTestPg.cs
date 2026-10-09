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
/// 与跨进程（dotnet test 每工程一个 testhost）都天然隔离，互不相扰。
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
        return Databases.GetOrAdd(name, _ =>
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
    }

    /// <summary>
    /// 用例间隔离：Respawn 清空该程序集库的全部业务表（保留三本迁移记账表）。
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
        // PG 库名上限 63 字节；abpadmin_ 前缀 + 程序集名（非字母数字折叠成 _、截断）足够区分且可读
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
    private readonly Lazy<Respawner> _respawner;

    internal TestPgDatabase(string name, string connectionString)
    {
        Name = name;
        ConnectionString = connectionString;
        // 适配器由 NpgsqlConnection 自动推断（Respawn 7）。删除图（表依赖序）建一次缓存复用，
        // 每次清表用新连接执行。三本迁移记账表必须活过清表——删了它们，
        // 下次启动会把已建表当空库重放迁移。
        _respawner = new Lazy<Respawner>(() =>
        {
            using var connection = new NpgsqlConnection(connectionString);
            connection.Open();
            return Respawner.CreateAsync(connection, new RespawnerOptions
            {
                TablesToIgnore = new Table[]
                {
                    "__EFMigrationsHistory",
                    "__BizTemplate_EFMigrationsHistory",
                    "__AbpAdminWebhooks_EFMigrationsHistory",
                },
            }).GetAwaiter().GetResult();
        });
    }

    public string Name { get; }

    public string ConnectionString { get; }

    /// <summary>
    /// 进程内一次性迁移守卫：同一库同一 key 只跑一次建表迁移（后续测试实例启动直接跳过，
    /// 迁移记账表保证幂等语义）。key 用 DbContext 名即可——守卫作用域本就限定在该库内。
    /// </summary>
    public void MigrateOnce(string key, Action migrate)
    {
        if (_migrated.ContainsKey(key))
        {
            return;
        }

        lock (this)
        {
            if (_migrated.ContainsKey(key))
            {
                return;
            }

            migrate();
            _migrated[key] = default;
        }
    }

    /// <summary>Respawn 清空业务表（同步阻塞；调用点在测试 Dispose，无同步上下文死锁面）。</summary>
    public void Reset()
    {
        using var connection = new NpgsqlConnection(ConnectionString);
        connection.Open();
        _respawner.Value.ResetAsync(connection).GetAwaiter().GetResult();
    }
}
