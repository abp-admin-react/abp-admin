using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TextTemplating;

namespace AbpAdmin.TextTemplates;

/// <summary>
/// 从数据库读取模板内容的 Contributor。
/// 必须排在虚拟文件 contributor 之前（通过 AbpTextTemplatingOptions.ContentContributors.Insert(0, ...) 注册）。
///
/// 回退顺序（contributor 内部完成，对所有模板统一生效，不区分 IsInlineLocalized）：
/// 请求文化 → 父文化（zh-Hans-CN → zh-Hans）→ 文化无关（CultureName == null 的覆盖记录）。
///
/// 与 ABP 引擎的分工：IsInlineLocalized 与 DefaultCultureName 是免费框架 TemplateDefinition
/// 的自有属性（并非 Pro 专属，见 Volo.Abp.TextTemplating.TemplateDefinition），引擎级回退
/// （TemplateContentProvider：请求文化 → 基础文化 → inline 取文化无关 / 否则取 DefaultCultureName）
/// 在本 contributor 返回 null 后依然生效。本实现刻意在 contributor 内提前回退并把解析后的
/// 最终内容按请求文化整体缓存：热路径固定一次版本读 + 一次内容读即得最终结果；
/// 代价是任何一级覆盖变化都会让该模板所有文化的缓存条目同时变脏——这正是缓存失效采用
/// 「整模板版本号作废」而非「按文化精确 Remove」的根因，见 TemplateContentCacheCleaner。
///
/// 返回非 null 时引擎即停止向后续 contributor 取值；三级全部落空返回 null，
/// 交还引擎链由虚拟文件等 contributor 兜底（此时本类写入负缓存哨兵防穿透）。
/// </summary>
public class StoredTemplateContentContributor : ITemplateContentContributor, ITransientDependency
{
    /// <summary>
    /// 负缓存哨兵值：DB 无覆盖记录时缓存该值（短 TTL），避免"未覆盖模板"每次渲染都击穿到 DB。
    /// 读回时翻译成 null 让 contributor 链继续回退到虚拟文件。正常模板内容不会等于该值。
    /// </summary>
    private const string NoContentSentinel = "\u0000__ABPADMIN_NO_STORED_TEMPLATE__";

    /// <summary>负缓存 TTL：无覆盖记录的缓存条目 1 分钟后过期。</summary>
    private static readonly TimeSpan NoContentCacheDuration = TimeSpan.FromMinutes(1);

    private readonly IRepository<TextTemplateContent, Guid> _contentRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly IDistributedCache<string, TemplateContentCacheKey> _cache;
    private readonly IDistributedCache<string> _versionCache;
    private readonly IOptions<AbpAdminTextTemplateOptions> _options;
    private readonly ILogger<StoredTemplateContentContributor> _logger;

    public StoredTemplateContentContributor(
        IRepository<TextTemplateContent, Guid> contentRepository,
        ICurrentTenant currentTenant,
        IDistributedCache<string, TemplateContentCacheKey> cache,
        IDistributedCache<string> versionCache,
        IOptions<AbpAdminTextTemplateOptions> options,
        ILogger<StoredTemplateContentContributor> logger)
    {
        _contentRepository = contentRepository;
        _currentTenant = currentTenant;
        _cache = cache;
        _versionCache = versionCache;
        _options = options;
        _logger = logger;
    }

    public virtual async Task<string?> GetOrNullAsync(TemplateContentContributorContext context)
    {
        var tenantId = _currentTenant.Id;
        var templateName = context.TemplateDefinition.Name;
        var requestedCulture = context.Culture;

        // 本方法是每次邮件/通知渲染的热路径：正常路径（命中/未命中/无覆盖）只留 Debug 日志，
        // Information 级会随发送量刷屏。无覆盖记录属正常回退（下一个 contributor 会接手），不算失败。
        _logger.LogDebug("[GetOrNullAsync] Template: {TemplateName}, Culture: {Culture}, Tenant: {TenantId}",
            templateName, requestedCulture ?? "null", tenantId?.ToString() ?? "host");

        // (TenantId, Name) 维度版本值：保存/恢复时被 Cleaner 替换，所有文化的旧条目一并作废。
        // 未初始化视为 "0"（首次访问），Cleaner 首次清理后开始轮转。
        var version = await _versionCache.GetAsync(TemplateContentCacheCleaner.VersionKey(tenantId, templateName)) ?? "0";
        var cacheKey = new TemplateContentCacheKey
        {
            TenantId = tenantId,
            Name = templateName,
            CultureName = requestedCulture,
            Version = version
        };

        var cachedContent = await _cache.GetAsync(cacheKey);
        if (cachedContent != null)
        {
            return cachedContent == NoContentSentinel ? null : cachedContent;
        }

        _logger.LogDebug("[GetOrNullAsync] Cache miss (version {Version}), querying database with fallback: requested -> parent -> invariant", version);

        // 从数据库查询，按回退顺序
        var content = await GetContentWithFallbackAsync(tenantId, templateName, requestedCulture);
        _logger.LogDebug("[GetOrNullAsync] DB fallback resolved. Template: {TemplateName}, Culture: {Culture}, HasOverride: {HasOverride}",
            templateName, requestedCulture ?? "null", content != null);

        // 写入缓存；无覆盖记录写负缓存哨兵（短 TTL），防止缓存穿透
        await _cache.SetAsync(
            cacheKey,
            content ?? NoContentSentinel,
            content == null
                ? new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = NoContentCacheDuration
                }
                : new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
                {
                    SlidingExpiration = _options.Value.ContentCacheSlidingExpiration
                });

        return content;
    }

    /// <summary>
    /// 按回退顺序查询模板内容。
    /// 当前实现：请求文化 → 父文化 → 文化无关
    /// </summary>
    private async Task<string?> GetContentWithFallbackAsync(Guid? tenantId, string templateName, string? requestedCulture)
    {
        var query = await _contentRepository.GetQueryableAsync();

        // 1. 尝试精确匹配请求文化
        if (!string.IsNullOrEmpty(requestedCulture))
        {
            var content = await GetContentAsync(query, tenantId, templateName, requestedCulture);
            if (content != null)
            {
                return content;
            }

            // 2. 尝试父文化
            var parentCulture = GetParentCulture(requestedCulture);
            if (!string.IsNullOrEmpty(parentCulture) && parentCulture != requestedCulture)
            {
                content = await GetContentAsync(query, tenantId, templateName, parentCulture);
                if (content != null)
                {
                    return content;
                }
            }
        }

        // 3. 回退到文化无关的内容
        return await GetContentAsync(query, tenantId, templateName, null);
    }

    private async Task<string?> GetContentAsync(
        IQueryable<TextTemplateContent> query,
        Guid? tenantId,
        string templateName,
        string? cultureName)
    {
        _logger.LogDebug("[GetContentAsync] Querying: Tenant={TenantId}, Name={Name}, Culture={Culture}",
            tenantId?.ToString() ?? "null", templateName, cultureName ?? "null");

        // 使用 IRepository 的 AsyncExecuter 来执行异步查询
        var stored = await _contentRepository.AsyncExecuter.FirstOrDefaultAsync(
            query.Where(x => x.TenantId == tenantId && x.Name == templateName && x.CultureName == cultureName));

        return stored?.Content;
    }

    /// <summary>
    /// 获取父文化，如 zh-Hans-CN → zh-Hans
    /// </summary>
    private static string? GetParentCulture(string cultureName)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            return culture.Parent?.Name;
        }
        catch
        {
            return null;
        }
    }
}
