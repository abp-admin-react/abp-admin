using AbpAdmin.Editions;
using AbpAdmin.MultiTenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Caching;
using Volo.Abp.Data;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.ModuleConfigurators;

/// <summary>
/// 多租户、版本缓存与数据库连接清单配置（自 AbpAdminDomainModule 拆出，注册等价搬移）。
/// </summary>
internal static class CachingAndTenancyConfigurator
{
    public static void ConfigureMultiTenancyAndCaching(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AbpMultiTenancyOptions>(options =>
        {
            options.IsEnabled = MultiTenancyConsts.IsEnabled;
        });

        // T4.4：Turnstile 校验的命名 HttpClient 注册已迁至 HttpRemoteConfigurator
        // （round4 审查：与声明式绑定、容错转换器同处，注册跟随消费方不变）

        // T2.8：版本解析缓存绝对过期（对齐 Pro，默认 1 小时），按缓存项类型配置
        var editionCacheOptions = configuration.GetSection(AbpAdminEditionCacheOptions.SectionName)
                                               .Get<AbpAdminEditionCacheOptions>() ?? new AbpAdminEditionCacheOptions();
        services.Configure<AbpAdminEditionCacheOptions>(configuration.GetSection(AbpAdminEditionCacheOptions.SectionName));
        services.Configure<AbpDistributedCacheOptions>(options =>
        {
            // 共享 Redis 的多应用隔离前缀（ABP 官方机制；rel-10.6.1 DistributedCacheKeyNormalizer
            // 反编译核实 + DistributedCacheKeyShapeTests 机器钉住的作用位置）：
            // ① 缓存条目键的 k: 段内——c:{CacheName},k:{KeyPrefix}{业务key}，多套部署的
            //    键同名不再互相覆盖；② stamp/hash 全局键的头部——{KeyPrefix}_Abp...Hash。
            // 键首的结构段 c:/t: 不变。留空 = 不隔离（独占实例/专属库）。切换后旧键读不到、
            // 缓存按需重建（无损）；DataProtection 密钥环与 SignalR ChannelPrefix 是独立键名
            // 体系，不随本前缀（但同源的部署判别键 App:InstanceDiscriminator 是本键的默认值——
            // 显式 DistributedCache:KeyPrefix 优先，未配时用判别键兜底，部署身份一处声明三处生效）。
            // 缓存监控的键空间守卫（AssertAbpKey/IsAllowedKey/BuildScanPattern）按本前缀收紧：
            // 配置后监控只可见/可删本应用的键（隔离是硬边界，不是页面过滤词）。
            var keyPrefix = configuration["DistributedCache:KeyPrefix"];
            if (string.IsNullOrWhiteSpace(keyPrefix))
            {
                keyPrefix = configuration["App:InstanceDiscriminator"]?.Trim();
            }
            if (!string.IsNullOrWhiteSpace(keyPrefix))
            {
                options.KeyPrefix = keyPrefix;
            }

            // 只设绝对过期（对齐 Pro）：主动失效覆盖改/删场景，自然到期最长等一个周期。
            // 不保留全局默认的 20 分钟滑动过期——滑动会让「1 小时内必失效」的承诺失效。
            options.ConfigureCache<TenantConfigurationCacheItem>(new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = editionCacheOptions.AbsoluteExpiration
            });
        });

        // T2.8：租户连接字符串管理的数据库清单。
        // 只有这里注册且 IsUsedByTenants = true 的数据库会出现在租户连接串管理界面；
        // "Default" 由专门的默认连接串字段承载，不在此列出。
        services.Configure<AbpDbConnectionOptions>(options =>
        {
            options.Databases.Configure("EasyAbpFileManagement", database =>
            {
                database.IsUsedByTenants = true;
            });
        });

        // 说明：租户/版本级功能编辑的授权策略（FeatureManagementOptions.ProviderPolicies）
        // 配置在 AbpAdminApplicationModule——AbpAdminPermissions 常量在 Application.Contracts，
        // Domain 引用不到（会循环依赖）。
    }
}
