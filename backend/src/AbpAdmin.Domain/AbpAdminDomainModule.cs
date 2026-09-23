using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.BackgroundWorkers;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement.Identity;
using Volo.Abp.SettingManagement;
using Volo.Abp.BlobStoring.Database;
using Volo.Abp.Caching;
using Volo.Abp.AuditLogging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Emailing;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Identity;
using Volo.Abp.Localization;
using Volo.Abp.OpenIddict;
using Volo.Abp.PermissionManagement.OpenIddict;
using Volo.Abp.TenantManagement;
using AbpAdmin.Localization;
using AbpAdmin.RateLimiting;
using AbpAdmin.ModuleConfigurators;
using AbpAdmin.ScheduledJobs;
using EasyAbp.Abp.Aliyun.Sms;
using EasyAbp.Abp.DataDictionary;
using EasyAbp.Abp.TencentCloud.Sms;
using EasyAbp.FileManagement;
using EasyAbp.Abp.SettingUi;
using EasyAbp.NotificationService;
using EasyAbp.NotificationService.Provider.Mailing;
using EasyAbp.NotificationService.Provider.Sms;
using EasyAbp.PaymentService;
using EasyAbp.PaymentService.Prepayment;
using EasyAbp.PaymentService.WeChatPay;
using Volo.Abp.Imaging;
using Volo.Abp.MailKit;
using Volo.Abp.TextTemplating;


namespace AbpAdmin;

[DependsOn(
    typeof(AbpAdminDomainSharedModule),
    typeof(AbpAuditLoggingDomainModule),
    typeof(AbpCachingModule),
    typeof(AbpBackgroundJobsDomainModule),
    typeof(AbpFeatureManagementDomainModule),
    typeof(AbpPermissionManagementDomainIdentityModule),
    typeof(AbpPermissionManagementDomainOpenIddictModule),
    typeof(AbpSettingManagementDomainModule),
    typeof(AbpEmailingModule),
    typeof(AbpIdentityDomainModule),
    typeof(AbpOpenIddictDomainModule),
    typeof(AbpTenantManagementDomainModule),
    typeof(BlobStoringDatabaseDomainModule),
    typeof(FileManagementDomainModule),
    typeof(AbpDataDictionaryDomainModule),
    typeof(AbpSettingUiDomainModule),
    typeof(AbpImagingSkiaSharpModule),
    // T3.5 通知服务：核心 + 邮件/短信 provider + MailKit + 短信厂商。
    // 注意不引 EasyAbp.Abp.Sms.TencentCloud 的包装模块 AbpSmsTencentCloudModule：
    // 它的凭据设置项 isEncrypted: false（1.21.0 反编译核实），不满足"凭据必须加密"；
    // 我们依赖底层 AbpTencentCloudSmsModule（提供 ITencentCloudApiRequester 与请求类型），
    // sender 用自写的 AbpAdminTencentCloudSmsSender（读加密设置项）。
    typeof(NotificationServiceDomainModule),
    typeof(NotificationServiceProviderMailingModule),
    typeof(NotificationServiceProviderSmsModule),
    typeof(AbpMailKitModule),
    typeof(AbpTencentCloudSmsModule),
    typeof(AbpAliyunSmsModule),
    typeof(PaymentServiceDomainModule),
    typeof(PaymentServicePrepaymentDomainModule),
    typeof(PaymentServiceWeChatPayDomainModule))]
public class AbpAdminDomainModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        // T2.5: 注册操作限流拦截器
        context.Services.OnRegistered(OperationRateLimitingInterceptorRegistrar.RegisterIfNeeded);
        // 防重复提交（对标 RuoYi @RepeatSubmit）
        context.Services.OnRegistered(RateLimiting.PreventDuplicateSubmitInterceptorRegistrar.RegisterIfNeeded);
    }

    // 问题4 修复：原 460 行模块按功能域拆分到 ModuleConfigurators/（静态扩展方法，注册等价搬移），
    // 本方法只留编排调用。各块的背景注释随代码迁走，OnApplicationInitialization 的启动校验保留原位。
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var services = context.Services;
        var configuration = services.GetConfiguration();

        services.ConfigureMultiTenancyAndCaching(configuration);   // 多租户/版本缓存/租户连接串清单
        services.ConfigureFileManagement(configuration);           // 文件容器与上传限额
        services.ConfigureBlobStoring(configuration);               // Blob 四提供商 switch
        services.ConfigurePayments();                                // Free/Prepayment/WeChatPay
        services.ConfigureDomainServiceReplacements();              // DbLanguageProvider/数据范围缓存
        services.ConfigureTextTemplatesAndGdpr(configuration);      // 模板 contributor/GDPR/CookieConsent
        services.ConfigureImaging(configuration);                   // SkiaSharp 压缩/缩放
        services.ConfigureOperationRateLimiting(configuration);     // 10 策略（强类型设置，含 GdprDownload）+ 核心服务
        services.ConfigureNotificationChannels(configuration);      // 站内信/MailKit/邮件开关
        services.ConfigureSmsResolvers();                            // ISmsSender → SmsSenderResolver
        services.ConfigureOutboundHttpRemote();                      // 出站 HTTP（HttpAgent）基础设施 + Domain 层声明式接口

        // Quartz 集群跨节点回退（背景与原理见装饰器注释）：框架默认 registry 是进程内的，
        // 持久化 store 里的 Quartz 作业被分到未注册该委托的节点会被静默丢弃。
        // 本模块的 ConfigureServices 晚于 Volo.Abp.BackgroundWorkers 模块的常规注册执行，
        // Replace 后 IDynamicBackgroundWorkerHandlerRegistry 解析到装饰器。
        services.AddSingleton<ScheduledJobWorkerHandlerRegistryDecorator>();
        services.Replace(ServiceDescriptor.Singleton<IDynamicBackgroundWorkerHandlerRegistry>(
            sp => sp.GetRequiredService<ScheduledJobWorkerHandlerRegistryDecorator>()));
    }
    // 注意：必须是同步的 OnApplicationInitialization，不能用 Async 重载——
    // ABP 的同步 Initialize 路径（AbpIntegratedTest 等测试基建走的就是这条）只会调同步
    // 生命周期方法；Async 重载在测试环境永远不会执行，下面这段注册会静默缺失。
    // 反方向是安全的：Async 路径会经基类默认实现调到这里的同步方法，宿主行为不变。
    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        // T3.1: 双向约束的启动期校验——头像白名单里每个扩展名都必须能被
        // FileSignatures 识别，对不上就快速失败（快速失败胜过运行期放行）
        var imagingOptions = context.ServiceProvider.GetRequiredService<IOptions<Imaging.AbpAdminImagingOptions>>().Value;
        var unrecognizable = Imaging.FileSignaturesImageContentValidator
            .GetUnrecognizableExtensions(imagingOptions.AvatarAllowedExtensions);
        if (unrecognizable.Count > 0)
        {
            throw new AbpInitializationException(
                $"Imaging:AvatarAllowedExtensions 里存在不能被 FileSignatures 识别的扩展名：" +
                $"{string.Join(", ", unrecognizable)}。请修正白名单或在 FileSignaturesImageContentValidator 补格式映射。");
        }

        // T2.5: 校验所有策略引用的具名分区解析器已注册（启动时失败而不是运行时发现）
        var rateLimitingOptions = context.ServiceProvider.GetRequiredService<IOptions<AbpAdminOperationRateLimitingOptions>>().Value;
        foreach (var (policyName, policy) in rateLimitingOptions.Policies)
        {
            foreach (var rule in policy.Rules)
            {
                if (rule.PartitionType == OperationRateLimitingPartitionType.Custom)
                {
                    if (rule.PartitionResolverName == null)
                    {
                        throw new AbpException($"操作限流策略 '{policyName}' 的规则 PartitionType 为 Custom 但 PartitionResolverName 为空。");
                    }
                    if (!rateLimitingOptions.PartitionKeyResolvers.ContainsKey(rule.PartitionResolverName))
                    {
                        throw new AbpException($"操作限流策略 '{policyName}' 引用了未注册的具名分区解析器 '{rule.PartitionResolverName}'。请在 AbpAdminOperationRateLimitingOptions 中通过 AddPartitionKeyResolver 注册。");
                    }
                }
            }
        }

        // T2.2: 为所有本地化资源添加数据库贡献者。
        // 贡献者是进程级常驻对象：必须给它应用根容器（IAbpApplication.ServiceProvider），
        // 不能给 context.ServiceProvider——ABP 的 InitializeModules 用 using CreateScope()
        // 跑模块初始化，该 scope 初始化结束即销毁（00-overview 6.5 节 captive dependency）。
        var localizationOptions = context.ServiceProvider.GetRequiredService<IOptions<AbpLocalizationOptions>>().Value;
        var rootServiceProvider = context.ServiceProvider.GetRequiredService<IAbpApplication>().ServiceProvider;
        var currentTenant = context.ServiceProvider.GetRequiredService<ICurrentTenant>();

        foreach (var resource in localizationOptions.Resources.Values)
        {
            resource.Contributors.Add(new DbLocalizationResourceContributor(rootServiceProvider, currentTenant));
        }

        // T3.3：T2.1 的 ExpiredAuditLogDeleterWorker 已删除，过期审计日志清理改由
        // AuditLogCleanupJobHandler（JobType = AbpAdmin.AuditLogCleanup）按 cron 执行。
    }
}
