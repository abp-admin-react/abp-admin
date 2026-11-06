using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using AbpAdmin;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Volo.Abp.AspNetCore.ExceptionHandling;

namespace AbpAdmin.ClientErrors;

internal static class ClientErrorPipelineHost
{
    private static readonly Lazy<Task<(WebApplication App, HttpClient Client)>> Lazy = new(CreateAsync);

    public static Task<(WebApplication App, HttpClient Client)> HostTask => Lazy.Value;

    private static async Task<(WebApplication App, HttpClient Client)> CreateAsync()
    {
        var environment = Environment.GetEnvironmentVariable("CLIENT_ERROR_TEST_ENVIRONMENT");
        if (string.IsNullOrWhiteSpace(environment))
        {
            environment = "Development";
        }

        if (environment is not ("Development" or "Production"))
        {
            throw new InvalidOperationException(
                "CLIENT_ERROR_TEST_ENVIRONMENT must be Development or Production.");
        }

        AbpAdminTestPg.TrySetHostOverrideEnvVar();
        var baseConn = RequiresHostDatabaseFactAttribute.TryResolveOverride()
                       ?? throw new InvalidOperationException(
                           "Client error HTTP pipeline tests require PostgreSQL."
                           + (AbpAdminTestPg.LastContainerStartError is { } error
                               ? " Container start failed: " + error
                               : string.Empty));
        var connString = await EnsureIsolatedSchemaAsync(baseConn, "abp_admin_clienterror");

        var builder = await HostUnderTest.CreateBuilderAsync(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = connString,
                ["AuthServer:UseDevelopmentCertificates"] = environment == "Production" ? "true" : null,
                ["Identity:AdminPassword"] = environment == "Production"
                    ? "ClientErrorPipeline!Test1"
                    : null,
                ["StringEncryption:DefaultPassPhrase"] = environment == "Production"
                    ? "ClientErrorPipeline-StringEncryption-Test"
                    : null,
                // 夹具只验证错误投影。本机/Production 叠加若打开 Redis，会在 TestServer 里拨号失败。
                ["Redis:IsEnabled"] = "false",
                ["SignalR:UseRedisBackplane"] = "false",
            },
            useAutofac: true,
            beforeAddApplication: servicesBuilder =>
            {
                servicesBuilder.Logging.AddProvider(ClientErrorTestLogBuffer.Instance);
                servicesBuilder.Services
                    .AddControllers()
                    .AddApplicationPart(typeof(ClientErrorFaultController).Assembly);
                servicesBuilder.Services.PostConfigure<AbpExceptionHandlingOptions>(options =>
                {
                    options.SendExceptionsDetailsToClients = true;
                    options.SendStackTraceToClients = true;
                });
            },
            environmentName: environment);

        builder.WebHost.UseTestServer();
        var app = builder.Build();
        await app.InitializeApplicationAsync();
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static async Task<string> EnsureIsolatedSchemaAsync(string connectionString, string schemaName)
    {
        var bare = connectionString.Contains("SearchPath=", StringComparison.OrdinalIgnoreCase)
            ? connectionString[..connectionString.IndexOf("SearchPath=", StringComparison.OrdinalIgnoreCase)].TrimEnd(';')
            : connectionString;

        await using var conn = new NpgsqlConnection(bare);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE SCHEMA IF NOT EXISTS {schemaName}", conn);
        await cmd.ExecuteNonQueryAsync();

        return bare.EndsWith(';') ? bare + $"SearchPath={schemaName}" : bare + $";SearchPath={schemaName}";
    }
}
