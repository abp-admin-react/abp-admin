using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.Modules;
using EasyAbp.Abp.SettingUi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.FeatureManagement;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Uow;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Modules.Tests;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class DeploymentModuleTests : AbpAdminTestBase<DeploymentModuleTestModule>
{
    private DeploymentModuleManager Manager => GetRequiredService<DeploymentModuleManager>();
    private IDeploymentModuleStateReader Reader => GetRequiredService<IDeploymentModuleStateReader>();

    [Fact]
    public async Task Legacy_Initialization_Is_Explicit_Global_And_Repeated_Seed_Preserves_Choice()
    {
        var initial = await Reader.GetAsync();
        initial.Initialized.ShouldBeTrue();
        initial.IsEnabled("TestOrders").ShouldBeTrue();
        var disabled = await Manager.UpdateAsync("TestOrders", false, initial.Version);
        await Manager.InitializeAsync();
        var after = await Reader.GetAsync();
        after.IsEnabled("TestOrders").ShouldBeFalse();
        after.Version.ShouldBe(disabled.Snapshot.Version);
        var definition = await GetRequiredService<ISettingDefinitionManager>().GetAsync(DeploymentModuleSettings.Enabled("TestOrders"));
        definition.DefaultValue.ShouldBe("false");
        definition.IsVisibleToClients.ShouldBeFalse();
        definition.Providers.OrderBy(x => x).ShouldBe(new[] { "D", "G" });
        definition.Properties.ContainsKey("Group1").ShouldBeFalse();
    }

    [Fact]
    public async Task New_Initialization_Persists_Explicit_False_And_Does_Not_Reapply_On_Restart()
    {
        await ClearStateAsync();
        GetRequiredService<IConfiguration>()["Modules:InitializationMode"] = "New";
        await Manager.InitializeAsync();
        var state = await Reader.GetAsync();
        state.Initialized.ShouldBeTrue();
        state.States.Values.ShouldAllBe(x => !x);
        await WithUnitOfWorkAsync(async () =>
        {
            var values = await GetRequiredService<ISettingRepository>().GetListAsync("G", null!);
            values.Where(x => x.Name.StartsWith("AbpAdmin.Modules.Test", StringComparison.Ordinal)).Count().ShouldBe(3);
        });
        await Manager.UpdateAsync("TestOrders", true, state.Version);
        await Manager.InitializeAsync();
        (await Reader.GetAsync()).IsEnabled("TestOrders").ShouldBeTrue();
    }

    [Fact]
    public async Task Newly_Installed_Module_Does_Not_Inherit_Original_Upgrade_Mode()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var repository = GetRequiredService<ISettingRepository>();
            var row = await repository.FindAsync(DeploymentModuleSettings.Enabled("TestIndependent"), "G", null!);
            await repository.DeleteAsync(row, autoSave: true);
        });
        await Manager.InitializeAsync();
        var result = await Reader.GetAsync();
        result.IsEnabled("TestIndependent").ShouldBeFalse();
        result.IsEnabled("TestOrders").ShouldBeTrue();
    }

    [Fact]
    public async Task Enable_Is_Atomic_With_Dependencies_And_Stale_Edit_Is_Rejected()
    {
        await ResetNewAsync();
        var initial = await Reader.GetAsync();
        var result = await Manager.UpdateAsync("TestOrders", true, initial.Version);
        result.Snapshot.IsEnabled("TestOrders").ShouldBeTrue();
        result.Snapshot.IsEnabled("TestCollection").ShouldBeTrue();
        result.Snapshot.Version.ShouldBe(initial.Version + 1);
        result.Plan.Changes.Count.ShouldBe(2);
        var conflict = await Should.ThrowAsync<BusinessException>(() => Manager.UpdateAsync("TestIndependent", true, initial.Version));
        conflict.Code.ShouldBe(DeploymentModuleErrorCodes.VersionConflict);
        var blocked = await Should.ThrowAsync<BusinessException>(() => Manager.UpdateAsync("TestCollection", false, result.Snapshot.Version));
        blocked.Code.ShouldBe(DeploymentModuleErrorCodes.DependencyBlocked);
        (await Reader.GetAsync()).Version.ShouldBe(result.Snapshot.Version);
        var replay = await Manager.UpdateAsync("TestOrders", true, initial.Version);
        replay.Snapshot.Version.ShouldBe(result.Snapshot.Version);
        replay.Plan.Changes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Version_Write_Failure_Rolls_Back_All_Module_Rows()
    {
        await ResetNewAsync();
        var initial = await Reader.GetAsync();
        var connectionString = AbpAdminTestPg.GetDatabase(typeof(DeploymentModuleTestModule).Assembly).ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"ALTER TABLE \"AbpSettings\" ADD CONSTRAINT module_test_fail_version CHECK (\"Name\" <> 'AbpAdmin.Modules.Version' OR \"Value\" <> '{initial.Version + 1}')", connection))
            await command.ExecuteNonQueryAsync();
        try
        {
            await Should.ThrowAsync<Exception>(() => Manager.UpdateAsync("TestOrders", true, initial.Version));
            var state = await Reader.GetAsync();
            state.Version.ShouldBe(initial.Version);
            state.IsEnabled("TestOrders").ShouldBeFalse();
            state.IsEnabled("TestCollection").ShouldBeFalse();
        }
        finally
        {
            await using var command = new NpgsqlCommand("ALTER TABLE \"AbpSettings\" DROP CONSTRAINT module_test_fail_version", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Concurrent_Changes_With_One_Version_Only_Commit_Once()
    {
        await ResetNewAsync();
        var initial = await Reader.GetAsync();
        async Task<string> Update(string id)
        {
            using var scope = ServiceProvider.CreateScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<DeploymentModuleManager>().UpdateAsync(id, true, initial.Version);
                return "committed";
            }
            catch (BusinessException ex) { return ex.Code!; }
        }
        var results = await Task.WhenAll(Task.Run(() => Update("TestOrders")), Task.Run(() => Update("TestIndependent")));
        results.Count(x => x == "committed").ShouldBe(1);
        results.Count(x => x == DeploymentModuleErrorCodes.VersionConflict).ShouldBe(1);
        (await Reader.GetAsync()).Version.ShouldBe(initial.Version + 1);
    }

    [Fact]
    public async Task SettingUi_Cannot_List_Or_Change_Module_State()
    {
        var initial = await Reader.GetAsync();
        var ui = GetRequiredService<ISettingUiAppService>();
        var name = DeploymentModuleSettings.Enabled("TestOrders");
        var groups = await ui.GroupSettingDefinitionsAsync();
        groups.SelectMany(group => group.SettingInfos)
            .ShouldNotContain(info => DeploymentModuleSettings.IsReserved(info.Name));
        await ui.SetSettingValuesAsync(new Dictionary<string, string> { [name] = "false" });
        await ui.ResetSettingValuesAsync([name]);
        var result = await Reader.GetAsync();
        result.Version.ShouldBe(initial.Version);
        result.IsEnabled("TestOrders").ShouldBeTrue();
    }

    [Fact]
    public async Task Tenant_Cannot_Write_And_Reading_Does_Not_Reuse_Tracked_Old_Values()
    {
        var initial = await Reader.GetAsync();
        using var ambient = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false);
        // Track the old row in the ambient context before a different transaction changes it.
        var old = await GetRequiredService<ISettingRepository>().FindAsync(DeploymentModuleSettings.Enabled("TestOrders"), "G", null!);
        old.Value.ShouldBe("true");
        await Manager.UpdateAsync("TestOrders", false, initial.Version);
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
        {
            var result = await Reader.GetAsync();
            result.IsEnabled("TestOrders").ShouldBeFalse();
            await Should.ThrowAsync<Volo.Abp.Authorization.AbpAuthorizationException>(() => Manager.UpdateAsync("TestOrders", true, result.Version));
        }
    }

    [Fact]
    public async Task Uninitialized_State_Does_Not_Admit_Or_Allow_Management_Writes()
    {
        await ClearStateAsync();
        var state = await Reader.GetAsync();
        state.Initialized.ShouldBeFalse();
        state.IsEnabled("TestOrders").ShouldBeFalse();
        var exception = await Should.ThrowAsync<BusinessException>(() => Manager.UpdateAsync("TestOrders", true, 0));
        exception.Code.ShouldBe(DeploymentModuleErrorCodes.NotInitialized);
    }

    [Fact]
    public async Task Reader_Uses_Host_Settings_With_An_Already_Open_Independent_Tenant_Database()
    {
        var tenantDatabase = "modules_tenant_" + Guid.NewGuid().ToString("N");
        var baseConnection = AbpAdminTestPg.ContainerConnectionString;
        await using var connection = new NpgsqlConnection(baseConnection);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand($"CREATE DATABASE {tenantDatabase}", connection)) await command.ExecuteNonQueryAsync();
        try
        {
            var tenantConnection = new NpgsqlConnectionStringBuilder(baseConnection) { Database = tenantDatabase }.ConnectionString;
            var tenantId = Guid.Empty;
            await WithUnitOfWorkAsync(async () =>
            {
                var tenant = await GetRequiredService<TenantManager>().CreateAsync("Module isolation test");
                tenant.SetDefaultConnectionString(tenantConnection);
                await GetRequiredService<ITenantRepository>().InsertAsync(tenant, autoSave: true);
                tenantId = tenant.Id;
            });
            using (GetRequiredService<ICurrentTenant>().Change(tenantId))
            using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: false))
            {
                var tenantContext = await GetRequiredService<IDbContextProvider<AbpAdminDbContext>>().GetDbContextAsync();
                await tenantContext.Database.OpenConnectionAsync();
                tenantContext.Database.GetDbConnection().Database.ShouldBe(tenantDatabase);
                var state = await Reader.GetAsync();
                state.Initialized.ShouldBeTrue();
                state.IsEnabled("TestOrders").ShouldBeTrue();
                GetRequiredService<ICurrentTenant>().Id.ShouldBe(tenantId);
            }
        }
        finally
        {
            await using var command = new NpgsqlCommand($"DROP DATABASE {tenantDatabase} WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task Failed_New_Initialization_Leaves_No_Marker_And_Retry_Keeps_New_Intent()
    {
        await ClearStateAsync();
        GetRequiredService<IConfiguration>()["Modules:InitializationMode"] = "New";
        await using var connection = new NpgsqlConnection(AbpAdminTestPg.GetDatabase(typeof(DeploymentModuleTestModule).Assembly).ConnectionString);
        await connection.OpenAsync();
        await using (var command = new NpgsqlCommand("ALTER TABLE \"AbpSettings\" ADD CONSTRAINT module_test_fail_init CHECK (\"Name\" <> 'AbpAdmin.Modules.Initialization')", connection))
            await command.ExecuteNonQueryAsync();
        try
        {
            await Should.ThrowAsync<Exception>(() => Manager.InitializeAsync());
            (await Reader.GetAsync()).Initialized.ShouldBeFalse();
            await WithUnitOfWorkAsync(async () =>
                (await GetRequiredService<ISettingRepository>().GetListAsync("G", null!))
                .ShouldNotContain(x => x.Name.StartsWith("AbpAdmin.Modules.", StringComparison.Ordinal)));
        }
        finally
        {
            await using var command = new NpgsqlCommand("ALTER TABLE \"AbpSettings\" DROP CONSTRAINT module_test_fail_init", connection);
            await command.ExecuteNonQueryAsync();
        }
        await Manager.InitializeAsync();
        var result = await Reader.GetAsync();
        result.Initialized.ShouldBeTrue();
        result.States.Values.ShouldAllBe(x => !x);
    }

    private async Task ResetNewAsync()
    {
        await ClearStateAsync();
        GetRequiredService<IConfiguration>()["Modules:InitializationMode"] = "New";
        await Manager.InitializeAsync();
    }

    private Task ClearStateAsync() => WithUnitOfWorkAsync(async () =>
    {
        var repository = GetRequiredService<ISettingRepository>();
        var rows = await repository.GetListAsync("G", null!);
        foreach (var row in rows.Where(x => x.Name.StartsWith("AbpAdmin.Modules.", StringComparison.Ordinal)))
            await repository.DeleteAsync(row, autoSave: true);
    });
}

[DependsOn(typeof(AbpAdminApplicationTestModule), typeof(AbpAdminEntityFrameworkCoreModule))]
public class DeploymentModuleTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Keep real transactions: the general EF test module deliberately disables them.
        context.Services.AddSingleton<IDeploymentModuleDefinitionProvider>(new TestModuleDefinitions());
        Configure<FeatureManagementOptions>(o => { o.SaveStaticFeaturesToDatabase = false; o.IsDynamicFeatureStoreEnabled = false; });
        Configure<PermissionManagementOptions>(o => { o.SaveStaticPermissionsToDatabase = false; o.IsDynamicPermissionStoreEnabled = false; });
        var database = AbpAdminTestPg.GetDatabase(typeof(DeploymentModuleTestModule).Assembly);
        database.MigrateOnce("AbpAdminDbContext", () =>
        {
            using var db = new AbpAdminDbContext(new DbContextOptionsBuilder<AbpAdminDbContext>().UseNpgsql(database.ConnectionString).Options);
            db.Database.Migrate();
        });
        Configure<AbpDbConnectionOptions>(o => o.ConnectionStrings.Default = database.ConnectionString);
    }
}

internal sealed class TestModuleDefinitions : IDeploymentModuleDefinitionProvider
{
    public IReadOnlyList<DeploymentModuleDefinition> GetDefinitions() =>
    [
        new("TestOrders", "Orders", ["TestCollection"]),
        new("TestCollection", "Collection"),
        new("TestIndependent", "Independent")
    ];
}

public class DeploymentModuleRegistryTests
{
    [Fact]
    public void Rejects_Missing_Cyclic_And_Duplicate_Definitions()
    {
        Should.Throw<AbpException>(() => new DeploymentModuleRegistry([new Definitions([new("A", "A", ["Missing"])])]));
        Should.Throw<AbpException>(() => new DeploymentModuleRegistry([new Definitions([new("A", "A", ["B"]), new("B", "B", ["A"])])]));
        Should.Throw<AbpException>(() => new DeploymentModuleRegistry([new Definitions([new("A", "A"), new("A", "A")])]));
    }
    private sealed class Definitions(IReadOnlyList<DeploymentModuleDefinition> definitions) : IDeploymentModuleDefinitionProvider
    {
        public IReadOnlyList<DeploymentModuleDefinition> GetDefinitions() => definitions;
    }
}
