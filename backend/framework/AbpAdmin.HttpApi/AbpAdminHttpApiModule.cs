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
            // 审计 F-E3/#4（2026-10-18）：匿名请求裁剪 setting.values（登录防线画像不下发）。
            // 无宿主依赖（只读 ICurrentUser），按本仓分层惯例（DataDictionary 同款）注册在
            // HttpApi 层＝引用本层的任何组合根默认获得该防线；CookieConsent/SignalR 因依赖
            // 宿主配置仍注册在宿主模块。注册在位由 AnonymousSettingTrimTests 钉死（GitHub
            // abpframework/abp#19879：贡献者不会被自动发现）。
            options.Contributors.Add(new AnonymousSettingTrimApplicationConfigurationContributor());
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
