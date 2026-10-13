using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Localization;

namespace AbpAdmin.Localization;

/// <summary>
/// 静态本地化文本基线提供器：枚举虚拟文件 json / 资源内联文本里的"原始静态翻译"。
///
/// 用途对标 ABP Pro LanguageManagement 的静态文本外部存储（SaveToExternalStore）：
/// 语言文本管理页要能看到资源里「从未被覆盖过」的 key（未翻译项），只查覆盖表是看不到的。
/// 这里不走"同步进表"方案，而是按需从静态贡献者里取：静态文本随代码发布天然最新，
/// 也不需要 Pro 的两张外部存储表与启动后台同步。
///
/// 实现要点：
/// 1. 过滤交给框架——LocalizationResourceContributorList.FillAsync 的 includeDynamicContributors=false
///    会跳过所有 IsDynamic 贡献者（本项目只有 DbLocalizationResourceContributor 是动态的 = DB 覆盖），
///    剩下的虚拟文件 json / 内联文本就是纯静态基线。
/// 2. 贡献者的 Initialize 由 AbpStringLocalizerFactory 在「首次为该资源创建 localizer」时才执行；
///    在那之前直接摸 Contributors 会撞 _virtualFileProvider == null 的 NRE——
///    所以先经工厂创建一次 localizer（有缓存，幂等便宜）再枚举。
/// </summary>
public class StaticLocalizationTextProvider : ITransientDependency
{
    private readonly IOptions<AbpLocalizationOptions> _localizationOptions;
    private readonly IStringLocalizerFactory _localizerFactory;

    public StaticLocalizationTextProvider(
        IOptions<AbpLocalizationOptions> localizationOptions,
        IStringLocalizerFactory localizerFactory)
    {
        _localizationOptions = localizationOptions;
        _localizerFactory = localizerFactory;
    }

    /// <summary>
    /// 按资源名查注册资源（本类内部使用；资源宇宙与 AbpLocalizationOptions 同源）。
    /// </summary>
    private LocalizationResourceBase? GetResourceOrNull(string resourceName)
    {
        return _localizationOptions.Value.Resources.Values
            .FirstOrDefault(x => x.ResourceName == resourceName);
    }

    /// <summary>
    /// 取静态基线文本（指定资源+文化，沿文化回退链合并：文化 → 父文化 → … → 中性文化，
    /// 越具体的文化优先）。文化名非法或资源不可枚举时返回空字典。
    /// 回退语义与 ABP 运行时（请求文化 → base culture → DefaultCulture → 继承资源）的刻意差异
    /// （本提供器服务"漏译发现"，不是运行时管线复刻）：
    /// ① 默认文化回退默认不并入——目标文化链上缺失的 key 显示空串、被列表 OnlyEmpty 计为未翻译；
    ///    <paramref name="includeDefaultCultureFallback"/> 供基准文化对照列使用（Pro 同款：
    ///    基准列包含默认文化与父文化回退），并入时放在最粗一层、具体层照常覆盖；
    /// ② 运行时只回退单步 base culture，这里走全 Parent 链（运行时口径的超集，
    ///    粗层值只是"多显示"，不会盖掉具体文化层）。
    /// </summary>
    public virtual async Task<Dictionary<string, string>> GetTextsAsync(
        string resourceName,
        string? cultureName,
        bool includeDefaultCultureFallback = false)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var resource = GetResourceOrNull(resourceName);
        if (resource == null || string.IsNullOrWhiteSpace(cultureName))
        {
            return result;
        }

        // 触发贡献者初始化（见类注释 2）；工厂有缓存，重复调用无成本。
        // ResourceType 在派生类 LocalizationResource 上（LocalizationResourceBase 没有）。
        // 非 typed 资源无法经工厂初始化虚拟文件 provider，FillAsync 会 NRE——
        // 与非法文化同款 fail-closed：返回空基线，不让管理页崩 500
        if (resource is not LocalizationResource concreteResource || concreteResource.ResourceType == null)
        {
            return result;
        }

        _localizerFactory.Create(concreteResource.ResourceType);

        // 回退链已排为从粗到细（后面的层覆盖前面的）；默认文化按运行时顺序并入最粗一层
        var chain = GetCultureFallbackChain(cultureName).ToList();
        if (includeDefaultCultureFallback
            && !string.IsNullOrEmpty(resource.DefaultCultureName)
            && !chain.Contains(resource.DefaultCultureName, StringComparer.Ordinal))
        {
            chain.Insert(0, resource.DefaultCultureName);
        }

        foreach (var culture in chain)
        {
            var dictionary = new Dictionary<string, LocalizedString>(StringComparer.Ordinal);
            await resource.Contributors.FillAsync(culture, dictionary, includeDynamicContributors: false);
            foreach (var (name, value) in dictionary)
            {
                result[name] = value.Value ?? string.Empty;
            }
        }

        return result;
    }

    /// <summary>
    /// 文化回退链，从最粗到最细：zh-Hans-CN → zh-Hans → zh → ""。
    /// Parent 链防御性限制层数并防自环（个别 ICU 实现的 neutral culture parent 指向自身）。
    /// </summary>
    private static IEnumerable<string> GetCultureFallbackChain(string cultureName)
    {
        var names = new List<string>();
        try
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            var guard = 0;
            while (culture != CultureInfo.InvariantCulture && !string.IsNullOrEmpty(culture.Name) && guard++ < 10)
            {
                names.Add(culture.Name);
                var parent = culture.Parent;
                if (string.IsNullOrEmpty(parent.Name) || parent.Name == culture.Name)
                {
                    break;
                }

                culture = parent;
            }
        }
        catch (CultureNotFoundException)
        {
            return Array.Empty<string>();
        }

        names.Reverse();
        return names;
    }
}
