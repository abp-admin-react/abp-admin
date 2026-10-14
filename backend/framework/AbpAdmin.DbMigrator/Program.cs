using System;
using System.IO;
using System.Linq;
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
                /* Single shared Host config (do not duplicate):
                 * ConnectionStrings / AuthServer / StringEncryption / OpenIddict live in
                 * HttpApi.Host/Configuration/*.json + thin appsettings.json (+ secrets).
                 * This project's appsettings.json keeps migrator-only keys (Quartz / Identity).
                 *
                 * Load order (later wins):
                 *  1. HttpApi.Host/Configuration/*.json   — baseline (SelfUrl/Cors/Redis/Authority)
                 *  2. HttpApi.Host/appsettings.json        — shared overlays (ops/business)
                 *  3. HttpApi.Host/appsettings.secrets.json — shared secrets
                 *  4. HttpApi.Host/appsettings.Development.json — Development only
                 *     (AbpAdmin_TestCli, extra redirect URIs, AllowDefaultAdminPassword).
                 *     Intentionally skip appsettings.Production.json (Redis multi-instance posture
                 *     would break migrator without Redis__Configuration).
                 *  5. this project's appsettings.json      — migrator-only overlays
                 *  6. environment variables                — highest (prod/CI)
                 *
                 * Containers/CI usually lack HttpApi.Host sources; inject DB via env vars.
                 * Run from this project directory (ContentRoot=CWD; hostDir = ../AbpAdmin.HttpApi.Host).
                 */
                var hostDir = Path.GetFullPath(
                    Path.Combine(hosting.HostingEnvironment.ContentRootPath, "..", "AbpAdmin.HttpApi.Host"));

                cfg.Sources.Clear();
                // Host Configuration/*.json first (SelfUrl/Cors/OpenIddict/AuthServer/Redis/connection baseline)
                var hostConfigDir = Path.Combine(hostDir, "Configuration");
                if (Directory.Exists(hostConfigDir))
                {
                    foreach (var file in Directory.EnumerateFiles(hostConfigDir, "*.json")
                                 .Where(f => !Path.GetFileName(f).Contains(".example", StringComparison.OrdinalIgnoreCase))
                                 .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                    {
                        cfg.AddJsonFile(file, optional: true, reloadOnChange: false);
                    }
                }
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
