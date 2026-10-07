using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/// <summary>
/// 权限定义运行时管理测试专用模块：仓库测试基线关闭了动态权限存储
/// （SaveStaticPermissionsToDatabase=false / IsDynamicPermissionStoreEnabled=false，见基线模块），
/// 但被测能力正是"动态定义写入 → 存储重读"这条链路，须在本模块重新开启。
/// 模块 ConfigureServices 在依赖之后执行，配置覆盖基线模块的关闭项。
/// </summary>
[DependsOn(typeof(AbpAdminEntityFrameworkCoreTestModule))]
public class PermissionDefinitionManagementTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<PermissionManagementOptions>(options =>
        {
            options.IsDynamicPermissionStoreEnabled = true;
        });
    }
}
