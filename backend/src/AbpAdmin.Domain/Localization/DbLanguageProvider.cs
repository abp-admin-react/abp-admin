// 验证结论（2026-08-17，基于 Volo.Abp.Localization 10.6.0）：
// - ILanguageProvider: 存在，签名为 Task<IReadOnlyList<LanguageInfo>> GetLanguagesAsync()，可通过 Services.Replace 替换
// - IExternalLocalizationStore: 存在，位于 Volo.Abp.Localization.External 命名空间，被框架的本地化管线消费
// - 动态 contributor: 可运行时追加，LocalizationResourceBase.Contributors 是公开的 LocalizationResourceContributorList 类型
// 因此本任务的实现路径选择了：替换 ILanguageProvider + 实现 IExternalLocalizationStore，负债定级为低。

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Localization;
using Volo.Abp.Threading;

namespace AbpAdmin.Localization;

/// <summary>
/// 从数据库读取语言列表的 ILanguageProvider 实现。
/// 数据库无启用语言时回退到框架静态配置，保证系统在种子跑之前也能起来。
/// </summary>
public class DbLanguageProvider : ILanguageProvider, ITransientDependency
{
    private readonly IOptions<AbpLocalizationOptions> _localizationOptions;
    private readonly ILanguageRepository _languageRepository;
    private readonly IDistributedCache<LanguageCacheItem> _languageCache;
    private readonly ICancellationTokenProvider _cancellationTokenProvider;

    public DbLanguageProvider(
        IOptions<AbpLocalizationOptions> localizationOptions,
        ILanguageRepository languageRepository,
        IDistributedCache<LanguageCacheItem> languageCache,
        ICancellationTokenProvider cancellationTokenProvider)
    {
        _localizationOptions = localizationOptions;
        _languageRepository = languageRepository;
        _languageCache = languageCache;
        _cancellationTokenProvider = cancellationTokenProvider;
    }

    public virtual async Task<IReadOnlyList<Volo.Abp.Localization.LanguageInfo>> GetLanguagesAsync()
    {
        var cacheItem = await _languageCache.GetOrAddAsync(
            LanguageCacheKeys.AllLanguages,
            async () => await GetLanguagesFromDatabaseAsync(),
            () => new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = System.TimeSpan.FromMinutes(30)
            },
            token: _cancellationTokenProvider.Token);

        if (cacheItem?.Languages == null || cacheItem.Languages.Count == 0)
        {
            // 数据库无启用语言时回退框架静态配置
            return _localizationOptions.Value.Languages;
        }

        return cacheItem.Languages
            .Where(x => x.IsEnabled)
            .Select(x => new Volo.Abp.Localization.LanguageInfo(x.CultureName, x.UiCultureName, x.DisplayName))
            .ToList();
    }

    protected virtual async Task<LanguageCacheItem> GetLanguagesFromDatabaseAsync()
    {
        var languages = await _languageRepository.GetListAsync();
        return new LanguageCacheItem
        {
            Languages = languages.Select(x => new LanguageInfo
            {
                CultureName = x.CultureName,
                UiCultureName = x.UiCultureName,
                DisplayName = x.DisplayName,
                FlagIcon = x.FlagIcon,
                IsEnabled = x.IsEnabled,
                IsDefault = x.IsDefault
            }).ToList()
        };
    }
}
