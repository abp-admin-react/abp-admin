using Volo.Abp.Settings;

namespace AbpAdmin.Modules;

public static class DeploymentModuleSettings
{
    private const string Prefix = "AbpAdmin.Modules.";
    public const string Version = Prefix + "Version";
    public const string Initialization = Prefix + "Initialization";
    public static string Enabled(string id) => $"{Prefix}{id}.Enabled";
    public static bool IsReserved(string name) => name.StartsWith(Prefix, System.StringComparison.Ordinal);
}

public sealed class DeploymentModuleSettingDefinitionProvider(DeploymentModuleRegistry registry) : SettingDefinitionProvider
{
    public override void Define(ISettingDefinitionContext context)
    {
        context.Add(Define(DeploymentModuleSettings.Version, "0"));
        context.Add(Define(DeploymentModuleSettings.Initialization, ""));
        foreach (var id in registry.Definitions.Keys)
            context.Add(Define(DeploymentModuleSettings.Enabled(id), "false"));
    }

    private static SettingDefinition Define(string name, string value) =>
        new(name, value, isVisibleToClients: false)
        {
            // No Group1/WithUi: generic SettingUi save/reset must never alter deployment state.
            Providers = { GlobalSettingValueProvider.ProviderName, DefaultValueSettingValueProvider.ProviderName }
        };
}
