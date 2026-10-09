using System.Runtime.CompilerServices;
using AbpAdmin;

namespace AbpAdmin.HttpApiHost;

/// <summary>
/// 程序集加载即把 Testcontainers PG 的连接串放进宿主覆盖环境变量：
/// <see cref="RequiresHostDatabaseFactAttribute"/> 的发现期门控与匿名端点扫查取串同源，
/// 完整宿主初始化落在本进程容器（专用 schema abp_admin_hosttest 内），不依赖外部 PG。
/// 容器起不来时静默——门控按「无覆盖」跳过真库用例，其余不碰库的用例照常执行。
/// </summary>
internal static class TestPgAssemblyInitializer
{
    [ModuleInitializer]
    internal static void Initialize() => AbpAdminTestPg.TrySetHostOverrideEnvVar();
}
