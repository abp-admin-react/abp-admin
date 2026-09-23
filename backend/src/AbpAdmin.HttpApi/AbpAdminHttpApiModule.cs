using Localization.Resources.AbpUi;
using AbpAdmin.Controllers;
using AbpAdmin.Imaging;
using AbpAdmin.Localization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Account;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.SettingManagement;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement.HttpApi;
using Volo.Abp.Localization;
using Volo.Abp.TenantManagement;
using EasyAbp.FileManagement;
using EasyAbp.Abp.SettingUi;
using EasyAbp.NotificationService;
using EasyAbp.PaymentService;
using EasyAbp.PaymentService.Prepayment;
using EasyAbp.PaymentService.WeChatPay;

namespace AbpAdmin;

 [DependsOn(
    typeof(AbpAdminApplicationContractsModule),
    typeof(AbpPermissionManagementHttpApiModule),
    typeof(AbpSettingManagementHttpApiModule),
    typeof(AbpAccountHttpApiModule),
    typeof(AbpIdentityHttpApiModule),
    typeof(AbpTenantManagementHttpApiModule),
    typeof(AbpFeatureManagementHttpApiModule),
    typeof(FileManagementHttpApiModule),
    typeof(AbpSettingUiHttpApiModule),
    typeof(NotificationServiceHttpApiModule),
    typeof(PaymentServiceHttpApiModule),
    typeof(PaymentServicePrepaymentHttpApiModule),
    typeof(PaymentServiceWeChatPayHttpApiModule)
    )]
public class AbpAdminHttpApiModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        ConfigureLocalization();

        // T3.1: ImageProcessingBusy（503）响应补 Retry-After 头
        Configure<MvcOptions>(options =>
        {
            options.Filters.Add<ImageProcessingBusyExceptionFilter>();
        });

        // 字典标签色白名单进 application-configuration（前端下拉的唯一来源，替代前端硬编码）
        Configure<AbpApplicationConfigurationOptions>(options =>
        {
            options.Contributors.Add(new DataDictionaryTagTypesApplicationConfigurationContributor());
        });
    }

    private void ConfigureLocalization()
    {
        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Get<AbpAdminResource>()
                .AddBaseTypes(
                    typeof(AbpUiResource)
                );
        });
    }
}
