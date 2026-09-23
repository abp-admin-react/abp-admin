using AbpAdmin.OperationLogs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Modularity;

namespace AbpAdmin.Settings;

/// <summary>
/// SettingUi 脱敏/操作日志测试的专用模块：在共享测试模块之上把 IOperationLogWriter
/// 换成录制替身。不放进 AbpAdminApplicationTestModule——EfCoreOperationLogWriterTests
/// 要测真实 writer 的落库行为（截断/净化/独立 UoW），全局替换会误伤它们。
/// </summary>
[DependsOn(typeof(AbpAdminApplicationTestModule))]
public class AbpAdminSettingUiMaskingTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.Replace(
            ServiceDescriptor.Singleton<IOperationLogWriter, RecordingOperationLogWriter>());

        // AbpAdminSettingUiAppService 依赖 IHttpContextAccessor（操作日志 IP/UA），
        // 测试基座没有 AspNetCore 的 Http 注册，补一个空 accessor（HttpContext 为 null → 字段落 null）
        context.Services.AddHttpContextAccessor();
    }
}
