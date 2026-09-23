using AbpAdmin.Notifications;
using AbpAdmin.Settings;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications.Notifications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreNotificationDispatcherTests : NotificationDispatcherTests<AbpAdminEntityFrameworkCoreTestModule>
{
}

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreMyNotificationAppServiceTests : MyNotificationAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{
}

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreNotificationManagementAppServiceTests : NotificationManagementAppServiceTests<AbpAdminEntityFrameworkCoreTestModule>
{
}

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreSmsSenderResolverTests : SmsSenderResolverTests<AbpAdminEntityFrameworkCoreTestModule>
{
}

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreBroadcastNotificationJobTests : BroadcastNotificationJobTests<AbpAdminEntityFrameworkCoreTestModule>
{
}

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class EfCoreSettingUiMaskingTests : SettingUiMaskingTests<EfCoreSettingUiMaskingTestModule>
{
}

/// <summary>
/// 脱敏测试专用启动模块：EFCore 测试模块 + IOperationLogWriter 录制替身
/// （操作日志断言需要；真实 writer 的行为由 EfCoreOperationLogWriterTests 自己测）。
/// </summary>
[DependsOn(typeof(AbpAdminEntityFrameworkCoreTestModule), typeof(AbpAdminSettingUiMaskingTestModule))]
public class EfCoreSettingUiMaskingTestModule : AbpModule
{
}
