using System;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Modules;

public sealed class DeploymentModuleStateReader(
    ISettingRepository repository, DeploymentModuleRegistry registry,
    ICurrentTenant currentTenant, IUnitOfWorkManager unitOfWorkManager)
    : IDeploymentModuleStateReader, ITransientDependency
{
    public async Task<DeploymentModuleSnapshot> GetAsync(CancellationToken cancellationToken = default)
    {
        using (currentTenant.Change(null))
        using (var uow = unitOfWorkManager.Begin(requiresNew: true, isTransactional: false))
        {
            var snapshot = await ReadCurrentUnitOfWorkAsync(cancellationToken);
            await uow.CompleteAsync(cancellationToken);
            return snapshot;
        }
    }

    // Writers call this inside their own fresh transaction and deployment-wide lock.
    public async Task<DeploymentModuleSnapshot> ReadCurrentUnitOfWorkAsync(CancellationToken cancellationToken = default)
    {
        var names = registry.Definitions.Keys.Select(DeploymentModuleSettings.Enabled)
            .Append(DeploymentModuleSettings.Version).Append(DeploymentModuleSettings.Initialization).ToArray();
        using var noTracking = repository.DisableTracking();
        var values = (await repository.GetListAsync(names, "G", null!, cancellationToken))
            .ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);
        var version = 0L;
        if (values.TryGetValue(DeploymentModuleSettings.Version, out var rawVersion)
            && (!long.TryParse(rawVersion, NumberStyles.None, CultureInfo.InvariantCulture, out version) || version < 0))
            throw new AbpException("Invalid deployment module state version.");
        var initialized = values.TryGetValue(DeploymentModuleSettings.Initialization, out var initialization)
                          && !string.IsNullOrWhiteSpace(initialization);
        var states = registry.Definitions.Keys.ToDictionary(id => id, id =>
            values.TryGetValue(DeploymentModuleSettings.Enabled(id), out var raw) &&
            bool.TryParse(raw, out var enabled) && enabled, StringComparer.Ordinal);
        return new DeploymentModuleSnapshot(version, initialized, states);
    }
}
