using AbpAdmin.Localization;
using Volo.Abp.AuditLogging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.Localization.ExceptionHandling;
using Volo.Abp.Validation.Localization;
using Volo.Abp.Modularity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.VirtualFileSystem;
using Volo.Abp.OpenIddict;
using Volo.Abp.BlobStoring.Database;
using Volo.Abp.TenantManagement;
using EasyAbp.FileManagement;
using EasyAbp.Abp.DataDictionary;
using EasyAbp.Abp.SettingUi;
using EasyAbp.Abp.SettingUi.Localization;
using EasyAbp.Abp.VerificationCode;
using EasyAbp.NotificationService;
using EasyAbp.PaymentService;
using EasyAbp.PaymentService.Prepayment;
using EasyAbp.PaymentService.WeChatPay;

namespace AbpAdmin;

[DependsOn(
    typeof(AbpAuditLoggingDomainSharedModule),
    typeof(AbpBackgroundJobsDomainSharedModule),
    typeof(AbpFeatureManagementDomainSharedModule),
    typeof(AbpPermissionManagementDomainSharedModule),
    typeof(AbpSettingManagementDomainSharedModule),
    typeof(AbpIdentityDomainSharedModule),
    typeof(AbpOpenIddictDomainSharedModule),
    typeof(AbpTenantManagementDomainSharedModule),
    typeof(BlobStoringDatabaseDomainSharedModule),
    typeof(FileManagementDomainSharedModule),
    typeof(AbpDataDictionaryDomainSharedModule),
    typeof(AbpSettingUiDomainSharedModule),
    typeof(AbpVerificationCodeModule),
    typeof(NotificationServiceDomainSharedModule),
    typeof(PaymentServiceDomainSharedModule),
    typeof(PaymentServicePrepaymentDomainSharedModule),
    typeof(PaymentServiceWeChatPayDomainSharedModule)
    )]
public class AbpAdminDomainSharedModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        AbpAdminGlobalFeatureConfigurator.Configure();
        AbpAdminModuleExtensionConfigurator.Configure();
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            options.FileSets.AddEmbedded<AbpAdminDomainSharedModule>();
        });

        Configure<AbpLocalizationOptions>(options =>
        {
            options.Resources
                .Add<AbpAdminResource>("en")
                .AddBaseTypes(typeof(AbpValidationResource))
                .AddVirtualJson("/Localization/AbpAdmin");

            // SettingUi 模块使用应用的本地化资源（分组名、设置显示名等）
            options.Resources
                .Get<SettingUiResource>()
                .AddVirtualJson("/Localization/AbpAdmin");

            // NotificationService 模块资源补丁：模块没本地化自己的权限组名 Permission:NotificationService
            options.Resources
                .Get<EasyAbp.NotificationService.Localization.NotificationServiceResource>()
                .AddVirtualJson("/Localization/AbpAdmin");

            options.Resources
                .Get<EasyAbp.PaymentService.Localization.PaymentServiceResource>()
                .AddVirtualJson("/Localization/AbpAdmin");

            options.DefaultResourceType = typeof(AbpAdminResource);
            
            options.Languages.Add(new LanguageInfo("en", "en", "English")); 
            options.Languages.Add(new LanguageInfo("ar", "ar", "Arabic")); 
            options.Languages.Add(new LanguageInfo("zh-Hans", "zh-Hans", "Chinese (Simplified)")); 
            options.Languages.Add(new LanguageInfo("zh-Hant", "zh-Hant", "Chinese (Traditional)")); 
            options.Languages.Add(new LanguageInfo("cs", "cs", "Czech")); 
            options.Languages.Add(new LanguageInfo("en-GB", "en-GB", "English (UK)")); 
            options.Languages.Add(new LanguageInfo("fi", "fi", "Finnish")); 
            options.Languages.Add(new LanguageInfo("fr", "fr", "French")); 
            options.Languages.Add(new LanguageInfo("de-DE", "de-DE", "German (Germany)")); 
            options.Languages.Add(new LanguageInfo("hi", "hi", "Hindi")); 
            options.Languages.Add(new LanguageInfo("hu", "hu", "Hungarian")); 
            options.Languages.Add(new LanguageInfo("is", "is", "Icelandic")); 
            options.Languages.Add(new LanguageInfo("it", "it", "Italian")); 
            options.Languages.Add(new LanguageInfo("pt-BR", "pt-BR", "Portuguese (Brazil)")); 
            options.Languages.Add(new LanguageInfo("ro-RO", "ro-RO", "Romanian (Romania)")); 
            options.Languages.Add(new LanguageInfo("ru", "ru", "Russian")); 
            options.Languages.Add(new LanguageInfo("sk", "sk", "Slovak")); 
            options.Languages.Add(new LanguageInfo("es", "es", "Spanish")); 
            options.Languages.Add(new LanguageInfo("sv", "sv", "Swedish")); 
            options.Languages.Add(new LanguageInfo("tr", "tr", "Turkish")); 

        });
        
        Configure<AbpExceptionLocalizationOptions>(options =>
        {
            options.MapCodeNamespace("AbpAdmin", typeof(AbpAdminResource));
        });
    }
}
