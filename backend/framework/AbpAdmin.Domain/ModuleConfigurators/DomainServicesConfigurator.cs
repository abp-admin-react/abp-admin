using AbpAdmin.TextTemplates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Volo.Abp.Localization;
using Volo.Abp.TextTemplating;

namespace AbpAdmin.ModuleConfigurators;

/// <summary>
/// 领域服务替换与文本模板/GDPR 选项配置（自 AbpAdminDomainModule 拆出，注册等价搬移）。
/// </summary>
internal static class DomainServicesConfigurator
{
    public static void ConfigureDomainServiceReplacements(this IServiceCollection services)
    {
        // 替换框架默认的 ILanguageProvider 为数据库驱动实现
        services.Replace(ServiceDescriptor.Transient<ILanguageProvider, Localization.DbLanguageProvider>());

        // 数据范围缓存 generation 用分布式缓存承载（DistributedDataScopeCacheGeneration）：
        // 多实例 + Redis 部署下范围变更的失效可跨实例传播；Redis 关闭时分布式缓存回落进程内存，
        // 与进程内实现等价。
        services.Replace(ServiceDescriptor.Transient<DataScopes.IDataScopeCacheGeneration, DataScopes.DistributedDataScopeCacheGeneration>());
    }

    public static void ConfigureTextTemplatesAndGdpr(this IServiceCollection services, IConfiguration configuration)
    {
        // 注册数据库存储的模板内容 contributor，必须排在虚拟文件 contributor 之前
        services.Configure<AbpTextTemplatingOptions>(options =>
        {
            options.ContentContributors.Insert(0, typeof(StoredTemplateContentContributor));
        });

        // 绑定文本模板选项
        services.Configure<AbpAdminTextTemplateOptions>(configuration.GetSection(AbpAdminTextTemplateOptions.SectionName));

        // 绑定 GDPR 与 Cookie Consent 选项
        services.Configure<Gdpr.AbpAdminGdprOptions>(configuration.GetSection(Gdpr.AbpAdminGdprOptions.SectionName));
        services.Configure<Gdpr.AbpAdminCookieConsentOptions>(configuration.GetSection(Gdpr.AbpAdminCookieConsentOptions.SectionName));
    }
}
