using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace AbpAdmin.Modules;

public interface IDeploymentModuleStateReader
{
    Task<DeploymentModuleSnapshot> GetAsync(CancellationToken cancellationToken = default);
}

/// <summary>A snapshot authorizes only one business operation; it is not a reusable admission token.</summary>
public sealed class DeploymentModuleSnapshot
{
    public long Version { get; }
    public bool Initialized { get; }
    public IReadOnlyDictionary<string, bool> States { get; }

    public DeploymentModuleSnapshot(long version, bool initialized, IReadOnlyDictionary<string, bool> states)
    {
        Version = version;
        Initialized = initialized;
        States = new ReadOnlyDictionary<string, bool>(new Dictionary<string, bool>(states));
    }

    public bool IsEnabled(string moduleId) => Initialized && States.TryGetValue(moduleId, out var enabled) && enabled;
}
