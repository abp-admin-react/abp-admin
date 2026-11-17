namespace AbpAdmin.Modules;

public static class DeploymentModuleIds
{
    public const string Orders = "Orders";
    public const string Collection = "Collection";
    public const string Chain = "Chain";
    public const string Finance = "Finance";
}

public static class DeploymentModuleErrorCodes
{
    public const string Disabled = "AbpAdmin:ModuleDisabled";
    public const string VersionConflict = "AbpAdmin:ModuleVersionConflict";
    public const string DependencyBlocked = "AbpAdmin:ModuleDependencyBlocked";
    public const string NotInitialized = "AbpAdmin:ModulesNotInitialized";
    public const string UnknownModule = "AbpAdmin:UnknownModule";
    public const string Busy = "AbpAdmin:ModuleManagementBusy";
}

public static class DeploymentModulePermissions
{
    public const string Default = "AbpAdmin.ModuleManagement";
    public const string Update = Default + ".Update";
}
