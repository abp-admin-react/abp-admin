using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Modules;

public sealed class DeploymentModuleDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public IReadOnlyList<string> Dependencies { get; }
    public string? ConfigurationPath { get; }
    public string? HistoryPath { get; }
    public bool EnabledOnUpgrade { get; }

    public DeploymentModuleDefinition(string id, string displayName, string[]? dependencies = null,
        string? configurationPath = null, string? historyPath = null, bool enabledOnUpgrade = true)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_'))
            throw new ArgumentException("Module IDs must contain ASCII letters, digits or underscores.", nameof(id));
        Id = id;
        DisplayName = Check.NotNullOrWhiteSpace(displayName, nameof(displayName));
        Dependencies = Array.AsReadOnly((dependencies ?? []).Distinct(StringComparer.Ordinal).ToArray());
        ConfigurationPath = configurationPath;
        HistoryPath = historyPath;
        EnabledOnUpgrade = enabledOnUpgrade;
    }
}

public interface IDeploymentModuleDefinitionProvider
{
    IReadOnlyList<DeploymentModuleDefinition> GetDefinitions();
}

/// <summary>Only installed modules contribute definitions. No assembly or module loading occurs here.</summary>
public sealed class DeploymentModuleRegistry : ISingletonDependency
{
    public IReadOnlyDictionary<string, DeploymentModuleDefinition> Definitions { get; }

    public DeploymentModuleRegistry(IEnumerable<IDeploymentModuleDefinitionProvider> providers)
    {
        var definitions = new Dictionary<string, DeploymentModuleDefinition>(StringComparer.Ordinal);
        foreach (var definition in providers.SelectMany(p => p.GetDefinitions()))
        {
            if (!definitions.TryAdd(definition.Id, definition))
                throw new AbpException($"Duplicate deployment module ID: {definition.Id}");
        }
        foreach (var definition in definitions.Values)
        {
            foreach (var dependency in definition.Dependencies)
                if (!definitions.ContainsKey(dependency))
                    throw new AbpException($"Module {definition.Id} depends on missing module {dependency}.");
        }
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in definitions.Keys) Visit(id);
        Definitions = new ReadOnlyDictionary<string, DeploymentModuleDefinition>(definitions);

        void Visit(string id)
        {
            if (visited.Contains(id)) return;
            if (!visiting.Add(id)) throw new AbpException($"Cyclic deployment module dependency at {id}.");
            foreach (var dependency in definitions[id].Dependencies) Visit(dependency);
            visiting.Remove(id);
            visited.Add(id);
        }
    }

    public IReadOnlyList<string> GetDependencyClosure(string id)
    {
        if (!Definitions.ContainsKey(id))
            throw new BusinessException(DeploymentModuleErrorCodes.UnknownModule).WithData("ModuleId", id);
        var result = new HashSet<string>(StringComparer.Ordinal);
        Add(id);
        return result.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        void Add(string current)
        {
            if (!result.Add(current)) return;
            foreach (var dependency in Definitions[current].Dependencies) Add(dependency);
        }
    }
}
