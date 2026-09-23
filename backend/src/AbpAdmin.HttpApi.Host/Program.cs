using System;
using System.Threading.Tasks;
using AbpAdmin.Elasticsearch;
using Elastic.Clients.Elasticsearch;
using Elastic.Ingest.Elasticsearch;
using Elastic.Serilog.Sinks;
using Elastic.Transport;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace AbpAdmin;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Async(c => c.File("Logs/logs.txt"))
            .WriteTo.Async(c => c.Console())
            .CreateBootstrapLogger();

        try
        {
            Log.Information("Starting AbpAdmin.HttpApi.Host.");
            var builder = WebApplication.CreateBuilder(args);
            // secrets 追加在环境变量之后会压过 env（ABP AddAppSettingsSecretsJson 的顺序问题）。
            // 这里再补一次 env 源置于链尾：生产/CI 环境变量（如 ConnectionStrings__Default）优先级最高，
            // 与本仓库"生产配置走环境变量注入"的约定一致。本项目的 appsettings.json 也是
            // DbMigrator 的共享配置源（数据库相关配置只维护在这里，DbMigrator 自动跟随）。
            // （builder.Configuration 与 Host.ConfigureAppConfiguration 操作同一 ConfigurationManager，
            // 此处直接追加到链尾，均在 ABP 模块初始化读配置之前生效。）
            builder.Configuration.AddJsonFile("appsettings.secrets.json", optional: true, reloadOnChange: false);
            builder.Configuration.AddEnvironmentVariables();

            // 相对路径 SQLite 连接串按 cwd 解析，从仓库根运行会在仓库外静默建库；
            // 以仓库根（AbpAdmin.slnx）为锚改写为绝对路径，须在模块初始化读取连接串之前，
            // 且必须在上面 secrets/env 源注册之后——归一化要看到它们提供的连接串
            if (AbpAdminDbPathNormalizer.TryNormalize(builder.Configuration, out var dbPath))
            {
                Log.Information("SQLite connection normalized to {DbPath}", dbPath);
            }

            builder.Host
                .UseAutofac()
                .UseSerilog((context, services, loggerConfiguration) =>
                {
                    loggerConfiguration
                        .ReadFrom.Configuration(context.Configuration)
                        .ReadFrom.Services(services)
                        // T3.2: Hub 的 query string 携带 access_token（浏览器 WebSocket 无法设请求头）。
                        // "Request starting" 在任何中间件之前就用原始 QueryString 记录，
                        // 管道内的剥离盖不住它，必须在日志出口对 /signalr-hubs 路径脱敏。
                        .Enrich.With<SignalR.SignalRQueryStringSanitizingEnricher>()
                        .WriteTo.Async(c => c.AbpStudio(services));

                    // T5：ES 运行日志 sink（Elastic 官方 sink，与 ABP 微服务模板同思路）。
                    // 不放 appsettings 的 WriteTo：secrets 配置文件里数组会整体替换，凭据也不应入仓库，
                    // 故在代码里按 "Elasticsearch" 配置节条件装配（启用判定走 options.IsUsable 单头谓词）。
                    // sink 自带缓冲与模板自举，不再包 Async。
                    var elasticsearch = context.Configuration
                        .GetSection(AbpAdminElasticsearchOptions.SectionName)
                        .Get<AbpAdminElasticsearchOptions>() ?? new AbpAdminElasticsearchOptions();
                    if (elasticsearch.IsUsable)
                    {
                        var dataStream = AbpAdminElasticsearchOptions.ParseLogDataStream(elasticsearch.LogDataStream);
                        if (dataStream is null)
                        {
                            // 配置非法必须告警并回退默认:叠加 sink 的 Silent 自举,静默改写 = 日志无声丢失
                            Log.Warning(
                                "Elasticsearch:LogDataStream 配置非法（{Value}，需 type-dataset-namespace 三段式），回退默认 {Default}",
                                elasticsearch.LogDataStream, AbpAdminElasticsearchOptions.DefaultLogDataStream);
                        }

                        // 回退值经同一解析函数从常量解析,与 options 默认值单头同源
                        var (dataStreamType, dataStreamDataSet, dataStreamNamespace) =
                            dataStream ?? AbpAdminElasticsearchOptions.ParseLogDataStream(
                                AbpAdminElasticsearchOptions.DefaultLogDataStream)!.Value;
                        loggerConfiguration.WriteTo.Elasticsearch(
                            new[] { new Uri(elasticsearch.Url) },
                            options =>
                            {
                                options.DataStream = new Elastic.Ingest.Elasticsearch.DataStreams.DataStreamName(
                                    dataStreamType, dataStreamDataSet, dataStreamNamespace);
                                options.BootstrapMethod = BootstrapMethod.Silent;
                                // 收紧入站缓冲(默认 10 万条 LogEvent 常驻内存,ES 变慢时最坏数十 MB),
                                // 满载后新日志被丢弃——日志链路优先保宿主内存预算
                                options.ConfigureChannel = channelOptions =>
                                {
                                    channelOptions.BufferOptions.InboundBufferMaxSize = 20_000;
                                };
                            },
                            transport => transport.Authentication(
                                new BasicAuthentication(elasticsearch.Username, elasticsearch.Password)));
                        Log.Information("Elasticsearch 日志 sink 已启用: {Url} -> {DataStream}",
                            elasticsearch.Url, $"{dataStreamType}-{dataStreamDataSet}-{dataStreamNamespace}");
                    }
                });
            await builder.AddApplicationAsync<AbpAdminHttpApiHostModule>();
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            if (ex is HostAbortedException)
            {
                throw;
            }

            Log.Fatal(ex, "Host terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }
}
