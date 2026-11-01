using Volo.Abp.Account;
using Volo.Abp.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.TenantManagement;
using EasyAbp.Abp.DataDictionary;
using EasyAbp.FileManagement;
using EasyAbp.Abp.SettingUi;
using EasyAbp.NotificationService;
using EasyAbp.PaymentService;
using EasyAbp.PaymentService.Prepayment;
using EasyAbp.PaymentService.WeChatPay;

namespace AbpAdmin;

[DependsOn(
    typeof(AbpAdminDomainSharedModule),
    typeof(AbpFeatureManagementApplicationContractsModule),
    typeof(AbpSettingManagementApplicationContractsModule),
    typeof(AbpIdentityApplicationContractsModule),
    typeof(AbpAccountApplicationContractsModule),
    typeof(AbpTenantManagementApplicationContractsModule),
    typeof(AbpPermissionManagementApplicationContractsModule),
    typeof(FileManagementApplicationContractsModule),
    typeof(AbpDataDictionaryApplicationContractsModule),
    typeof(AbpSettingUiApplicationContractsModule),
    typeof(NotificationServiceApplicationContractsModule),
    typeof(PaymentServiceApplicationContractsModule),
    typeof(PaymentServicePrepaymentApplicationContractsModule),
    typeof(PaymentServiceWeChatPayApplicationContractsModule)
)]
public class AbpAdminApplicationContractsModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        AbpAdminDtoExtensions.Configure();
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // Account 模块资源补丁：上游缺 TextTemplate:Abp.Account.PasswordResetLink 的本地化
        // （文本模板页的显示名按 AccountResource 解析，缺 key 时回落成原始键名）。
        // 补丁 JSON 复用 Domain.Shared 的 /Localization/AbpAdmin 虚拟路径（已在虚拟文件系统注册）。
        Configure<Volo.Abp.Localization.AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<Volo.Abp.Account.Localization.AccountResource>()
                .AddVirtualJson("/Localization/AbpAdmin");
        });
    }
}
