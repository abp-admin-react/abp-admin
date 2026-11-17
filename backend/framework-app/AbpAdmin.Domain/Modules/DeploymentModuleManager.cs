using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.DistributedLocking;
using Volo.Abp.MultiTenancy;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Modules;

public sealed record DeploymentModuleUpdateResult(DeploymentModuleSnapshot Snapshot, DeploymentModuleChangePlan Plan);

/// <summary>All state mutations, including bootstrap, use this lock and the same host transaction.</summary>
public class DeploymentModuleManager(
    DeploymentModuleRegistry registry, DeploymentModuleStateReader reader,
    ISettingManager settingManager, ISettingRepository repository,
    IAbpDistributedLock distributedLock, IUnitOfWorkManager unitOfWorkManager,
    ICurrentTenant currentTenant, IConfiguration configuration) : ITransientDependency
{
    private string LockName => $"AbpAdmin:DeploymentModules:{configuration["App:InstanceDiscriminator"] ?? "Default"}";

    public async Task<DeploymentModuleUpdateResult> UpdateAsync(string moduleId, bool enabled, long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        AssertHost();
        await using var handle = await distributedLock.TryAcquireAsync(LockName, TimeSpan.FromSeconds(15), cancellationToken);
        if (handle == null) throw new BusinessException(DeploymentModuleErrorCodes.Busy);
        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var snapshot = await reader.ReadCurrentUnitOfWorkAsync(cancellationToken);
        var plan = DeploymentModuleChanges.Plan(registry, snapshot, moduleId, enabled);
        // A replay is harmless only if the complete target (including enable dependencies) already holds.
        if (plan.CanApply && plan.Changes.Count == 0)
        {
            await uow.CompleteAsync(cancellationToken);
            return new(snapshot, plan);
        }
        if (snapshot.Version != expectedVersion)
            throw new BusinessException(DeploymentModuleErrorCodes.VersionConflict);
        if (!plan.CanApply)
            throw new BusinessException(DeploymentModuleErrorCodes.DependencyBlocked)
                .WithData("ModuleIds", string.Join(",", plan.BlockingModuleIds));
        foreach (var change in plan.Changes)
            await WriteAsync(DeploymentModuleSettings.Enabled(change.ModuleId), change.IsEnabled ? "true" : "false");
        var nextVersion = checked(snapshot.Version + 1);
        await WriteAsync(DeploymentModuleSettings.Version, nextVersion.ToString(CultureInfo.InvariantCulture));
        await uow.CompleteAsync(cancellationToken);
        var states = snapshot.States.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        foreach (var change in plan.Changes) states[change.ModuleId] = change.IsEnabled;
        return new(new DeploymentModuleSnapshot(nextVersion, true, states), plan);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        AssertHost();
        // Keep New in provisioning configuration until a successful initialization. Absence is legacy Upgrade.
        var requestedMode = configuration["Modules:InitializationMode"] ?? "Upgrade";
        if (requestedMode is not ("New" or "Upgrade"))
            throw new AbpException("Modules:InitializationMode must be New or Upgrade.");
        await using var handle = await distributedLock.TryAcquireAsync(LockName, TimeSpan.FromSeconds(30), cancellationToken);
        if (handle == null) throw new BusinessException(DeploymentModuleErrorCodes.Busy);
        using var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var snapshot = await reader.ReadCurrentUnitOfWorkAsync(cancellationToken);
        var rows = await repository.GetListAsync(registry.Definitions.Keys.Select(DeploymentModuleSettings.Enabled).ToArray(), "G", null!, cancellationToken);
        var existingNames = rows.Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        var states = snapshot.States.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
        var changed = !snapshot.Initialized;
        foreach (var definition in registry.Definitions.Values)
        {
            var name = DeploymentModuleSettings.Enabled(definition.Id);
            if (existingNames.Contains(name)) continue;
            // New modules in an initialized deployment never inherit the original legacy Upgrade intent.
            var enabled = !snapshot.Initialized && requestedMode == "Upgrade" && definition.EnabledOnUpgrade;
            states[definition.Id] = enabled;
            await WriteAsync(name, enabled ? "true" : "false");
            changed = true;
        }
        foreach (var definition in registry.Definitions.Values.Where(d => states[d.Id]))
        {
            if (definition.Dependencies.Any(id => !states[id]))
                throw new AbpException($"Module initialization would enable {definition.Id} without its dependencies. Existing explicit values were preserved.");
        }
        if (!snapshot.Initialized)
            await WriteAsync(DeploymentModuleSettings.Initialization, $"1:{requestedMode}");
        if (changed)
            await WriteAsync(DeploymentModuleSettings.Version, checked(snapshot.Version + 1).ToString(CultureInfo.InvariantCulture));
        await uow.CompleteAsync(cancellationToken);
    }

    private Task WriteAsync(string name, string value) => settingManager.SetAsync(name, value, "G", null!, forceToSet: true);
    private void AssertHost()
    {
        if (currentTenant.Id.HasValue) throw new AbpAuthorizationException();
    }
}

public sealed class DeploymentModuleDataSeedContributor(DeploymentModuleManager manager) : IDataSeedContributor, ITransientDependency
{
    public Task SeedAsync(DataSeedContext context) => context.TenantId.HasValue ? Task.CompletedTask : manager.InitializeAsync();
}
