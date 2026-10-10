using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace AbpAdmin.DbMigrator;

class Program
{
    static async Task Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Volo.Abp", LogEventLevel.Warning)
#if DEBUG
                .MinimumLevel.Override("AbpAdmin", LogEventLevel.Debug)
#else
                .MinimumLevel.Override("AbpAdmin", LogEventLevel.Information)
#endif
                .Enrich.FromLogContext()
            // 文件日志滚动策略与 HttpApi.Host/appsettings.json 的 File sink 严格一致：
            // 按天分文件（Logs/logsYYYYMMDD.txt）、单日 100MB 再分段、只保留最近 30 个文件。
            // ABP 官方模板默认不滚动、单文件无限累积，迁移器多轮执行会持续膨胀，不可沿用；
            // 两处策略须同步修改（Host 侧注释有相同说明）。
            .WriteTo.Async(c => c.File(
                "Logs/logs.txt",
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 100 * 1024 * 1024,
                retainedFileCountLimit: 30))
            .WriteTo.Async(c => c.Console())
            .CreateLogger();

        await CreateHostBuilder(args).RunConsoleAsync();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hosting, cfg) =>
            {
                /* 单一配置源（不要搞两套）：
                 * 数据库相关配置（ConnectionStrings:Default / AuthServer / StringEncryption
                 * 等共享项）只维护在 HttpApi.Host/appsettings.json（+ appsettings.secrets.json），
                 * 本项目 appsettings.json 仅保留迁移器专属配置（Quartz / Identity）；
                 * OpenIddict 客户端清单同样统一在 Host 基座：种子读组合后的 OpenIddict:Applications 节。
                 *
                 * 生效顺序（后者覆盖前者）：
                 *  1. HttpApi.Host/appsettings.json        —— 共享基座（切换数据库只改这里）
                 *  2. HttpApi.Host/appsettings.secrets.json —— 共享凭证
                 *  3. HttpApi.Host/appsettings.Development.json —— 仅 Development 环境加载：
                 *     开发专属 OIDC 通道（AbpAdmin_TestCli、demo/多端口回调）与
                 *     Identity:AllowDefaultAdminPassword 只在这里生效——生产环境不种入、
                 *     默认口令直接 fail-fast。刻意不加载 appsettings.Production.json：
                 *     那份文件的 Redis:IsEnabled=true 等多实例姿态属于宿主——一次性迁移器
                 *     没有 Redis__Configuration 时会拿着基座默认串（127.0.0.1:6379）去连，
                 *     建库串行化的分布式锁首用即失败，迁移器被无关姿态破坏。
                 *  4. 本项目 appsettings.json               —— 迁移器专属覆盖
                 *  5. 环境变量                               —— 最高（生产/CI 覆盖，含 secrets）
                 *
                 * 容器/CI 场景通常没有 HttpApi.Host 源码目录，数据库配置必须走环境变量注入。
                 * ⚠ 运行姿势：必须在本工程目录内 dotnet run（ContentRoot=CWD，下方的
                 *   hostDir 按 ../AbpAdmin.HttpApi.Host 相对解析——从 backend/ 根目录运行时
                 *   该路径不存在，两个 optional JSON 被静默跳过，启动落在 PG 占位串预检上）。
                 */
                var hostDir = Path.GetFullPath(
                    Path.Combine(hosting.HostingEnvironment.ContentRootPath, "..", "AbpAdmin.HttpApi.Host"));

                cfg.Sources.Clear();
                cfg.AddJsonFile(Path.Combine(hostDir, "appsettings.json"), optional: true, reloadOnChange: false);
                cfg.AddJsonFile(Path.Combine(hostDir, "appsettings.secrets.json"), optional: true, reloadOnChange: false);
                if (hosting.HostingEnvironment.IsDevelopment())
                {
                    cfg.AddJsonFile(Path.Combine(hostDir, "appsettings.Development.json"), optional: true, reloadOnChange: false);
                }
                cfg.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
                cfg.AddEnvironmentVariables();
            })
            .ConfigureLogging((context, logging) => logging.ClearProviders())
            .ConfigureServices((hostContext, services) =>
            {
                services.AddHostedService<DbMigratorHostedService>();
            });
}
