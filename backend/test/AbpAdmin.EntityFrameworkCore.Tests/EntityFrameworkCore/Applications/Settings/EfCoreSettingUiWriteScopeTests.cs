using AbpAdmin.Settings;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.Settings;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications.Settings;

/// <summary>
/// SettingUi 写入面作用域测试（安全审计 run-2 C-3）的 EFCore 具体化：
/// 启动模块在 EFCore 测试模块之上注册测试专用设置定义 provider。
/// </summary>
[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreSettingUiWriteScopeTests : SettingUiWriteScopeTests<EfCoreSettingUiWriteScopeTestModule>
{
}

[DependsOn(typeof(AbpAdminEntityFrameworkCoreTestModule))]
public class EfCoreSettingUiWriteScopeTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // 设置定义经 AbpSettingOptions.DefinitionProviders 注册（与权限定义的
        // AbpPermissionOptions 同构；测试定义只在本模块的启动链内生效）
        Configure<AbpSettingOptions>(options =>
        {
            options.DefinitionProviders.Add<SettingUiWriteScopeTestSettingProvider>();
        });
    }
}
