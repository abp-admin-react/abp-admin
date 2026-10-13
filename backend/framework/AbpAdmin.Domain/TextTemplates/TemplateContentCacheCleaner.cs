using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.TextTemplates;

/// <summary>
/// 模板内容缓存清理服务。
/// 保存覆盖或恢复默认时必须作废缓存。方案与 DbExternalLocalizationStore 同款：
/// 内容缓存 key 拼进 (TenantId, Name) 维度的版本值，这里把版本号替换成新随机值，
/// 该模板当前租户下**所有文化**（含文化无关与负缓存哨兵条目）一次性作废，
/// 旧条目靠 TTL 消亡——不再枚举硬编码文化清单（清单外文化曾有一个 TTL 的脏读窗口）。
/// </summary>
public class TemplateContentCacheCleaner : ITransientDependency
{
    private readonly IDistributedCache<string> _versionCache;
    private readonly ICurrentTenant _currentTenant;
    private readonly IOptions<AbpAdminTextTemplateOptions> _options;

    public TemplateContentCacheCleaner(
        IDistributedCache<string> versionCache,
        ICurrentTenant currentTenant,
        IOptions<AbpAdminTextTemplateOptions> options)
    {
        _versionCache = versionCache;
        _currentTenant = currentTenant;
        _options = options;
    }

    /// <summary>
    /// 作废指定模板在当前租户下所有文化的缓存：替换版本号即可，无需逐文化 Remove。
    /// 必须在数据库写入提交之后调用（写入口挂在 UnitOfWork.OnCompleted），
    /// 否则"版本已换、事务未提交"窗口里并发读会按新版本 key 回填旧值。
    /// 版本条目必须显式给**不短于内容条目**的过期时间：ABP 默认全局选项是 20 分钟滑动过期，
    /// 若版本条目先于内容条目消亡，读取端会回退到初始版本 "0"，而最后一次保存前写入的
    /// v0 内容条目可能仍在自己的滑动窗口内——恰好复活本次改动要消灭的脏读。
    /// 两者取同一时长且每次渲染都被读（滑动窗口同步续期），空闲超时后一起冷启动，语义安全。
    /// </summary>
    public virtual async Task ClearCacheAsync(string templateName)
    {
        await _versionCache.SetAsync(
            VersionKey(_currentTenant.Id, templateName),
            Guid.NewGuid().ToString("N"),
            new DistributedCacheEntryOptions
            {
                SlidingExpiration = _options.Value.ContentCacheSlidingExpiration
            });
    }

    public static string VersionKey(Guid? tenantId, string templateName)
        => $"AbpAdmin:TemplateContent:Version:{tenantId?.ToString() ?? "host"}:{templateName}";
}
