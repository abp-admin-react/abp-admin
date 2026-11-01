using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.Caching;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Localization;
using Volo.Abp.Localization.External;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Threading;

namespace AbpAdmin.Localization;

/// <summary>
/// 数据库本地化文本存储（(tenant, resource, culture) 维度缓存 + 版本号失效）。
/// 运行时管线的 DB 文本实际经 DbLocalizationResourceContributor（挂在每个资源的
/// contributor 链上，IsDynamic=true）进入 FillAsync/GetOrNull；本类实现的
/// IExternalLocalizationStore 是框架工厂的资源查找扩展点（资源不在静态注册表时被
/// AbpStringLocalizerFactory 询问，本项目资源全注册、恒返回 null，保留作为 Pro 同款抽象）。
/// 租户级覆盖优先于 host 级覆盖，host 级覆盖优先于虚拟文件 json 里的静态文本。
/// </summary>
public class DbExternalLocalizationStore : IExternalLocalizationStore, ITransientDependency
{
    private readonly ILanguageTextRepository _languageTextRepository;
    private readonly IDistributedCache<LanguageTextCacheItem> _textCache;
    private readonly IDistributedCache<string> _versionCache;
    private readonly ICancellationTokenProvider _cancellationTokenProvider;
    private readonly IOptions<AbpLocalizationOptions> _localizationOptions;
    private readonly IDataFilter<IMultiTenant> _multiTenantFilter;

    public DbExternalLocalizationStore(
        ILanguageTextRepository languageTextRepository,
        IDistributedCache<LanguageTextCacheItem> textCache,
        IDistributedCache<string> versionCache,
        ICancellationTokenProvider cancellationTokenProvider,
        IOptions<AbpLocalizationOptions> localizationOptions,
        IDataFilter<IMultiTenant> multiTenantFilter)
    {
        _languageTextRepository = languageTextRepository;
        _textCache = textCache;
        _versionCache = versionCache;
        _cancellationTokenProvider = cancellationTokenProvider;
        _localizationOptions = localizationOptions;
        _multiTenantFilter = multiTenantFilter;
    }

    public virtual LocalizationResourceBase? GetResourceOrNull(string resourceName)
    {
        // 返回静态配置中的资源，数据库覆盖通过 DbLocalizationResourceContributor 实现
        return _localizationOptions.Value.Resources.Values.FirstOrDefault(x => x.ResourceName == resourceName);
    }

    public virtual async Task<LocalizationResourceBase?> GetResourceOrNullAsync(string resourceName)
    {
        return await Task.FromResult(GetResourceOrNull(resourceName));
    }

    public virtual async Task<string[]> GetResourceNamesAsync()
    {
        // 返回静态配置中的所有资源名称
        return await Task.FromResult(_localizationOptions.Value.Resources.Keys.ToArray());
    }

    public virtual async Task<LocalizationResourceBase[]> GetResourcesAsync()
    {
        return await Task.FromResult(_localizationOptions.Value.Resources.Values.ToArray());
    }

    /// <summary>
    /// 从数据库获取本地化文本。先查当前租户覆盖行，未命中再查 host 行。
    /// 缓存 key 里拼了资源+文化维度的版本值：host 或租户写入后版本被替换，
    /// 所有租户的旧合并条目整体作废（host 行合并进了每个租户的缓存值，
    /// 按租户逐 key 清理需要枚举租户集合，不可行）。
    /// 返回值是缓存条目内部的字典引用：调用方只读，严禁修改——
    /// 改动会污染该 (resource, culture) 下全体租户的缓存直到 TTL。
    /// </summary>
    public virtual async Task<Dictionary<string, string>> GetTextsAsync(
        Guid? tenantId,
        string resourceName,
        string cultureName)
    {
        var version = await GetVersionAsync(resourceName, cultureName);
        var cacheKey = LanguageCacheKeys.LanguageTexts(tenantId, resourceName, cultureName, version);

        var cacheItem = await _textCache.GetOrAddAsync(
            cacheKey,
            async () => await GetTextsFromDatabaseAsync(tenantId, resourceName, cultureName),
            () => new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
            },
            token: _cancellationTokenProvider.Token);

        return cacheItem?.Texts ?? new Dictionary<string, string>();
    }

    /// <summary>
    /// 批量取多个资源的生效覆盖文本（管理页跨资源列表专用；运行时管线单资源路径走 GetTextsAsync）。
    /// 版本号读取与文本缓存命中各合并为一次批量往返（ABP 官方 GetManyAsync/GetOrAddManyAsync，
    /// Redis 集成下单次 MGET）；缓存 miss 时批式工厂内顺序查库回填——同一 scoped DbContext
    /// 不允许并发，ABP 批式工厂（一次传全部缺失 key）正好保证顺序。版本 key 的整体作废语义不变。
    /// 返回值与 GetTextsAsync 同款约束：缓存条目内部字典引用，调用方只读、严禁修改。
    /// </summary>
    public virtual async Task<Dictionary<string, Dictionary<string, string>>> GetManyTextsAsync(
        Guid? tenantId,
        IReadOnlyCollection<string> resourceNames,
        string cultureName)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var resources = resourceNames.Distinct(StringComparer.Ordinal).ToList();
        if (resources.Count == 0)
        {
            return result;
        }

        // 1) 版本号一次批量取（缺失 = "0"，与单资源路径同语义）
        var versionKeys = resources.ToDictionary(
            r => r, r => LanguageCacheKeys.LanguageTextVersion(r, cultureName), StringComparer.Ordinal);
        var versions = await _versionCache.GetManyAsync(
            versionKeys.Values, token: _cancellationTokenProvider.Token);
        var versionByKey = versions.ToDictionary(x => x.Key, x => x.Value ?? "0", StringComparer.Ordinal);

        // 2) 文本缓存一次批量取/回填，key 仍拼版本值
        var cacheKeys = resources.ToDictionary(
            r => r,
            r => LanguageCacheKeys.LanguageTexts(
                tenantId, r, cultureName, versionByKey.GetValueOrDefault(versionKeys[r], "0")),
            StringComparer.Ordinal);
        var resourceByKey = cacheKeys.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);

        var cached = await _textCache.GetOrAddManyAsync(
            cacheKeys.Values,
            async missingKeys =>
            {
                var filled = new List<KeyValuePair<string, LanguageTextCacheItem>>();
                foreach (var key in missingKeys)
                {
                    filled.Add(new KeyValuePair<string, LanguageTextCacheItem>(
                        key,
                        await GetTextsFromDatabaseAsync(tenantId, resourceByKey[key], cultureName)));
                }

                return filled;
            },
            () => new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30)
            },
            token: _cancellationTokenProvider.Token);

        foreach (var pair in cached)
        {
            result[resourceByKey[pair.Key]] = pair.Value?.Texts ?? new Dictionary<string, string>();
        }

        // 工厂未返回的 key（理论不可达，防御）也给空字典，保证每个请求资源都有条目
        foreach (var resource in resources)
        {
            result.TryAdd(resource, new Dictionary<string, string>());
        }

        return result;
    }

    private async Task<string> GetVersionAsync(string resourceName, string cultureName)
    {
        return await _versionCache.GetAsync(
            LanguageCacheKeys.LanguageTextVersion(resourceName, cultureName),
            token: _cancellationTokenProvider.Token) ?? "0";
    }

    protected virtual async Task<LanguageTextCacheItem> GetTextsFromDatabaseAsync(
        Guid? tenantId,
        string resourceName,
        string cultureName)
    {
        var texts = new Dictionary<string, string>();

        // 先查 host 行（TenantId == null）。租户上下文下 IMultiTenant 筛选器是严格相等，
        // 不过滤条件不会自动 fallback——必须显式 Disable 筛选器才查得到 host 行。
        List<LanguageText> hostTexts;
        using (_multiTenantFilter.Disable())
        {
            hostTexts = await _languageTextRepository.GetListAsync(null, resourceName, cultureName);
        }
        foreach (var text in hostTexts)
        {
            texts[text.Name] = text.Value;
        }

        // 再查租户覆盖行，覆盖 host 的值
        if (tenantId.HasValue)
        {
            var tenantTexts = await _languageTextRepository.GetListAsync(tenantId, resourceName, cultureName);
            foreach (var text in tenantTexts)
            {
                texts[text.Name] = text.Value;
            }
        }

        return new LanguageTextCacheItem { Texts = texts };
    }

    /// <summary>
    /// 使指定资源+文化的文本缓存失效（所有租户的合并条目一并作废）。
    /// 版本值用"替换成新随机值"而不是"读出来 +1"：两个并发写各自读到同一旧值、
    /// 双双写入相同新值时，后一个写入的失效会被吞掉。
    /// 必须在数据库写入提交之后调用（写入口挂在 UnitOfWork.OnCompleted 里），
    /// 否则"版本已换、事务未提交"窗口里并发读会按新版本 key 回填旧值。
    /// </summary>
    public virtual async Task InvalidateCacheAsync(string resourceName, string cultureName)
    {
        await _versionCache.SetAsync(
            LanguageCacheKeys.LanguageTextVersion(resourceName, cultureName),
            Guid.NewGuid().ToString("N"),
            token: _cancellationTokenProvider.Token);
    }
}
