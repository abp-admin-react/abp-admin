using System.Collections.Generic;
using System.Linq;
using Volo.Abp;

namespace AbpAdmin.Modules;

public sealed record DeploymentModuleChange(string ModuleId, bool WasEnabled, bool IsEnabled);
public sealed record DeploymentModuleChangePlan(long Version, IReadOnlyList<DeploymentModuleChange> Changes,
    IReadOnlyList<string> BlockingModuleIds)
{
    public bool CanApply => BlockingModuleIds.Count == 0;
}

public static class DeploymentModuleChanges
{
    public static DeploymentModuleChangePlan Plan(DeploymentModuleRegistry registry,
        DeploymentModuleSnapshot snapshot, string moduleId, bool enabled)
    {
        var closure = registry.GetDependencyClosure(moduleId);
        if (!snapshot.Initialized) throw new BusinessException(DeploymentModuleErrorCodes.NotInitialized);
        var blockers = enabled ? [] : registry.Definitions.Keys
            .Where(id => id != moduleId && snapshot.IsEnabled(id) && registry.GetDependencyClosure(id).Contains(moduleId))
            .OrderBy(id => id).ToArray();
        var targets = enabled ? closure : new[] { moduleId };
        var changes = targets.Where(id => snapshot.IsEnabled(id) != enabled)
            .Select(id => new DeploymentModuleChange(id, snapshot.IsEnabled(id), enabled)).ToArray();
        return new DeploymentModuleChangePlan(snapshot.Version, changes, blockers);
    }
}
