using System.Runtime.CompilerServices;
using AbpAdmin;

internal static class ClientErrorPipelineTestPgInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => AbpAdminTestPg.TrySetHostOverrideEnvVar();
}
