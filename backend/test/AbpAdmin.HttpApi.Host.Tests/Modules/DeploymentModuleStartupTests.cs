using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.Data;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.Modules.Tests;

public class DeploymentModuleStartupTests
{
    [RequiresHostDatabaseFact]
    public async Task UpToDate_Schema_Still_Initializes_Module_State_And_Preserves_Admin_Choices()
    {
        var baseConnection = AbpAdminTestPg.ContainerConnectionString;
        var schema = "module_startup_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(baseConnection);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection)) await command.ExecuteNonQueryAsync();
        var moduleConnection = new NpgsqlConnectionStringBuilder(baseConnection) { SearchPath = schema }.ConnectionString;
        try
        {
            using (var db = new AbpAdminDbContext(new DbContextOptionsBuilder<AbpAdminDbContext>().UseNpgsql(moduleConnection).Options))
                await db.Database.MigrateAsync();
            var builder = await HostUnderTest.CreateBuilderAsync(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = moduleConnection,
                ["Database:AutoMigrateOnStartup"] = "true",
                ["Modules:InitializationMode"] = "Upgrade",
                ["Redis:IsEnabled"] = "false"
            }, useAutofac: true);
            var schemas = Substitute.For<IAbpAdminDbSchemaMigrator>();
            schemas.HasPendingAsync().Returns(false);
            builder.Services.RemoveAll<IAbpAdminDbSchemaMigrator>();
            builder.Services.AddSingleton(schemas);
            await using var app = builder.Build();
            var hostModule = new AbpAdminHttpApiHostModule();
            await hostModule.MigrateSchemaIfConfiguredAsync(new ApplicationInitializationContext(app.Services));
            using var scope = app.Services.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<IDeploymentModuleStateReader>();
            var initial = await reader.GetAsync();
            initial.Initialized.ShouldBeTrue();
            initial.IsEnabled(DeploymentModuleIds.Orders).ShouldBeTrue();
            var changed = await scope.ServiceProvider.GetRequiredService<DeploymentModuleManager>()
                .UpdateAsync(DeploymentModuleIds.Orders, false, initial.Version);
            await hostModule.MigrateSchemaIfConfiguredAsync(new ApplicationInitializationContext(app.Services));
            var repeated = await reader.GetAsync();
            repeated.Version.ShouldBe(changed.Snapshot.Version);
            repeated.IsEnabled(DeploymentModuleIds.Orders).ShouldBeFalse();
            await schemas.DidNotReceive().MigrateAsync();
        }
        finally
        {
            await using var command = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
