using Volo.Abp.Auditing;
using Volo.Abp.Modularity;

namespace AbpAdmin.EntityFrameworkCore.Applications;

/// <summary>
/// 实体回滚测试专用模块：EntityHistorySelectors.AddAllEntities()（全实体变更记录）只在
/// Host 模块配置，测试基线没有——而"回滚产生新的受审计变更（前滚闭环）"正是被测行为，
/// 须在此按 Host 同款配置开启。仅本模块的测试实例开启，不影响测试库其他用例。
/// </summary>
[DependsOn(typeof(AbpAdminEntityFrameworkCoreTestModule))]
public class EntityRestoreTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAuditingOptions>(options =>
        {
            options.IsEnabled = true;
            options.EntityHistorySelectors.AddAllEntities();
        });
    }
}
