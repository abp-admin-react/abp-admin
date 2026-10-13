using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Localization;

namespace AbpAdmin.Localization;

[Authorize(AbpAdminPermissions.LanguageTexts.Default)]
public class LanguageTextAppService : ApplicationService, ILanguageTextAppService
{
    private readonly ILanguageTextRepository _languageTextRepository;
    private readonly DbExternalLocalizationStore _localizationStore;
    private readonly StaticLocalizationTextProvider _staticTextProvider;
    private readonly IOptions<AbpLocalizationOptions> _localizationOptions;

    public LanguageTextAppService(
        ILanguageTextRepository languageTextRepository,
        DbExternalLocalizationStore localizationStore,
        StaticLocalizationTextProvider staticTextProvider,
        IOptions<AbpLocalizationOptions> localizationOptions)
    {
        _languageTextRepository = languageTextRepository;
        _localizationStore = localizationStore;
        _staticTextProvider = staticTextProvider;
        _localizationOptions = localizationOptions;
    }

    /// <summary>
    /// 语言文本列表：覆盖行 + 静态基线合并视图（对标 Pro 的静态文本外部存储），
    /// 把从未覆盖过的静态 key 一并纳入，翻译人员能发现漏译。
    /// 资源可选：不传 = 跨全部注册资源列出（Pro 同款，表格以 ResourceName 列区分来源）。
    /// 两个口径：Value=生效值（本层覆盖 &gt; host 覆盖 &gt; 静态基线；静态基线沿目标文化父链，
    /// 刻意不含框架 DefaultCulture 回退——链上缺失的 key 显示空串并被 OnlyEmpty 计为未翻译）；
    /// IsOverridden=当前上下文是否有覆盖行（不含 host 的行）——「恢复默认」只删当前层，
    /// 前端以它决定按钮是否可点。OnlyEmpty 按生效值过滤，即真正的"未翻译过滤"。
    /// </summary>
    public virtual async Task<PagedResultDto<LanguageTextDto>> GetListAsync(GetLanguageTextsInput input)
    {
        // 目标文化必选（DTO [Required]，页面默认选中启用语言）；资源可选，空 = 全部注册资源
        var resourceNames = string.IsNullOrWhiteSpace(input.ResourceName)
            ? _localizationOptions.Value.Resources.Keys.ToList()
            : new List<string> { input.ResourceName };

        // 生效值：一次批量取 store 合并缓存（版本号读取与命中路径各一次批量往返，
        // miss 时批式工厂内顺序回源；ABP 官方 GetManyAsync/GetOrAddManyAsync），
        // 覆盖口径与本地化管线同源。对租户上下文，这里包含 host 的覆盖行——
        // host 覆盖对租户生效是管线本意，不能改。
        // 静态基线逐资源走贡献者内存缓存（FillAsync 结果已按文化缓存，成本是字典拷贝），
        // 沿目标文化父链、刻意不含框架 DefaultCulture 回退（运行时链全 miss 会回退
        // DefaultCulture 显示英文，这里不跟随——链上缺失的 key 显示空串、被 OnlyEmpty
        // 计为未翻译，正是漏译发现需要的口径，见 StaticLocalizationTextProvider）。
        var effectiveOverridesByResource = await _localizationStore.GetManyTextsAsync(
            CurrentTenant.Id, resourceNames, input.CultureName);
        var staticTextsByResource = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var resource in resourceNames)
        {
            staticTextsByResource[resource] = await _staticTextProvider.GetTextsAsync(resource, input.CultureName);
        }

        // 基准文化对照 = 基准文化的生效值（与 Value 同一合并口径：本层覆盖 > host 覆盖 > 静态基线），
        // 基准文化被覆盖时对照列才不失真；自定义 key 在基准文化无静态值、有覆盖时也取得到。
        // 与 Pro 同款，基准列的静态回退链并入资源默认文化（目标列不并入，保住"空=未翻译"）
        var baseTextsByResource = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(input.BaseCultureName))
        {
            // 基准覆盖行同样一次批量取；基准=目标时直接复用已取数据
            var baseOverrides = input.BaseCultureName == input.CultureName
                ? effectiveOverridesByResource
                : await _localizationStore.GetManyTextsAsync(
                    CurrentTenant.Id, resourceNames, input.BaseCultureName);

            foreach (var resource in resourceNames)
            {
                baseTextsByResource[resource] = MergeEffectiveTexts(
                    await _staticTextProvider.GetTextsAsync(
                        resource, input.BaseCultureName, includeDefaultCultureFallback: true),
                    baseOverrides.GetValueOrDefault(
                        resource, new Dictionary<string, string>(StringComparer.Ordinal)));
            }
        }

        // IsOverridden 用当前上下文自己的覆盖行（多租户过滤器自动限定：host 上下文 =
        // TenantId == null 的行，租户上下文 = 本租户的行）。资源为空时一次查全，
        // 避免按资源 N 次查询。多查这一次是必要的：store 的合并结果区分不出"哪层给的值"。
        var contextOverrideNamesByResource = await GetContextOverrideNamesAsync(
            resourceNames, input.CultureName);

        // 逐资源把静态 key 与覆盖 key 取并集展开成行（自定义覆盖 key 不在静态资源里也能列出）
        IEnumerable<LanguageTextDto> rows = resourceNames.SelectMany(resource =>
        {
            var staticTexts = staticTextsByResource[resource];
            var effectiveOverrides = effectiveOverridesByResource[resource];
            var baseTexts = baseTextsByResource.GetValueOrDefault(
                resource, new Dictionary<string, string>(StringComparer.Ordinal));
            var contextNames = contextOverrideNamesByResource.GetValueOrDefault(
                resource, new HashSet<string>(StringComparer.Ordinal));

            return staticTexts.Keys
                .Union(effectiveOverrides.Keys, StringComparer.Ordinal)
                .Select(name =>
                {
                    // 生效值看合并结果（host 覆盖也生效），IsOverridden 只看当前上下文那层
                    var hasEffectiveOverride = effectiveOverrides.TryGetValue(name, out var overrideValue);
                    return new LanguageTextDto
                    {
                        ResourceName = resource,
                        CultureName = input.CultureName,
                        Name = name,
                        Value = hasEffectiveOverride
                            ? overrideValue!
                            : staticTexts.GetValueOrDefault(name, string.Empty),
                        BaseValue = baseTexts.TryGetValue(name, out var baseValue) ? baseValue : null,
                        IsOverridden = contextNames.Contains(name),
                    };
                });
        });

        // 过滤后一次性物化：避免惰性 SelectMany 链被 Count() 和分页各枚举一遍
        // （跨资源宇宙 ~2700 行，双重投影意味着每请求双倍 DTO 分配）
        var filtered = ApplyFilter(rows, input).ToList();

        return new PagedResultDto<LanguageTextDto>(
            filtered.Count,
            ApplySorting(filtered, input.Sorting)
                .Skip(input.SkipCount)
                .Take(input.MaxResultCount)
                .ToList());
    }

    /// <summary>
    /// Sorting 白名单（官方 PagedAndSortedResultRequestDto 契约的内存视图实现）：
    /// 只支持 ResourceName/Name/Value/BaseValue，Ordinal 比较（null BaseValue 升序在前）；
    /// 未知字段静默忽略（排序不是安全边界，白名单已挡注入面），全部未知时回退默认序。
    /// </summary>
    private static readonly Dictionary<string, (Func<LanguageTextDto, string?> Selector, StringComparer Comparer)>
        SortKeys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["resourceName"] = (x => x.ResourceName, StringComparer.Ordinal),
            ["name"] = (x => x.Name, StringComparer.Ordinal),
            ["value"] = (x => x.Value, StringComparer.Ordinal),
            ["baseValue"] = (x => x.BaseValue, StringComparer.Ordinal),
        };

    private static IEnumerable<LanguageTextDto> ApplySorting(List<LanguageTextDto> rows, string? sorting)
    {
        IOrderedEnumerable<LanguageTextDto>? ordered = null;
        if (!string.IsNullOrWhiteSpace(sorting))
        {
            foreach (var token in sorting.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || !SortKeys.TryGetValue(parts[0], out var key))
                {
                    continue;
                }

                var descending = parts.Length > 1 && parts[1].Equals("desc", StringComparison.OrdinalIgnoreCase);
                ordered = ordered == null
                    ? (descending
                        ? rows.OrderByDescending(key.Selector, key.Comparer)
                        : rows.OrderBy(key.Selector, key.Comparer))
                    : (descending
                        ? ordered.ThenByDescending(key.Selector, key.Comparer)
                        : ordered.ThenBy(key.Selector, key.Comparer));
            }
        }

        // 默认序（不传或 token 全部未知）：ResourceName → Name，保证分页切片稳定
        return ordered ?? rows.OrderBy(x => x.ResourceName, StringComparer.Ordinal)
            .ThenBy(x => x.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// 当前上下文的覆盖 key 集合，按资源分组（IsOverridden 的口径来源）。
    /// 查询走环境多租户过滤器：host 上下文查 TenantId == null 的行，租户上下文查本租户的行。
    /// 跨资源模式（资源列表 = 服务端注册表）天然排除已注销资源的孤儿行；
    /// 单资源模式下资源名是客户端传入值，不经过注册表校验，孤儿行也会命中（与覆盖值可读性一致）。
    /// </summary>
    private async Task<Dictionary<string, HashSet<string>>> GetContextOverrideNamesAsync(
        List<string> resourceNames, string cultureName)
    {
        var query = await _languageTextRepository.GetQueryableAsync();
        var overrideRows = await AsyncExecuter.ToListAsync(
            query.Where(x => x.CultureName == cultureName && resourceNames.Contains(x.ResourceName))
                .Select(x => new { x.ResourceName, x.Name }));

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var row in overrideRows)
        {
            if (!result.TryGetValue(row.ResourceName, out var names))
            {
                names = new HashSet<string>(StringComparer.Ordinal);
                result[row.ResourceName] = names;
            }

            names.Add(row.Name);
        }

        return result;
    }

    /// <summary>
    /// 覆盖行（数据库实体）→ DTO 的唯一映射路径。覆盖行恒有 Id 且 IsOverridden=true；
    /// BaseValue 需要静态基线提供器才能算出，此路径不查（列表合并视图才提供）。
    /// 不用 Mapperly profile：DTO 新增的 IsOverridden/BaseValue 在实体上没有对应源成员，
    /// 映射器只给 RMG020 警告而静默产出 IsOverridden=false 的矛盾数据，不如收拢为手写映射。
    /// </summary>
    private static LanguageTextDto ToOverrideRowDto(LanguageText x)
    {
        return new LanguageTextDto
        {
            Id = x.Id,
            ResourceName = x.ResourceName,
            CultureName = x.CultureName,
            Name = x.Name,
            Value = x.Value,
            IsOverridden = true
        };
    }

    /// <summary>
    /// 合并视图的过滤（key/生效值模糊匹配 + 只看未翻译）。
    /// 过滤在内存中对全宇宙做（合并视图来自静态+覆盖两路，无法下推数据库），
    /// 大小写不敏感（OrdinalIgnoreCase）；资源为空的跨资源模式同样适用。
    /// </summary>
    private static IEnumerable<LanguageTextDto> ApplyFilter(
        IEnumerable<LanguageTextDto> rows, GetLanguageTextsInput input)
    {
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            rows = rows.Where(x => x.Name.Contains(input.Filter, StringComparison.OrdinalIgnoreCase)
                || x.Value.Contains(input.Filter, StringComparison.OrdinalIgnoreCase));
        }

        if (input.OnlyEmpty)
        {
            rows = rows.Where(x => string.IsNullOrEmpty(x.Value));
        }

        return rows;
    }

    /// <summary>
    /// 生效值合并：静态基线打底、覆盖行覆盖（与 Value 的三层层级同一顺序）。
    /// 仅用于基准文化对照列——目标文化的合并在 store 缓存里已完成。
    /// </summary>
    private static Dictionary<string, string> MergeEffectiveTexts(
        Dictionary<string, string> staticTexts, Dictionary<string, string> overrides)
    {
        var merged = new Dictionary<string, string>(staticTexts, StringComparer.Ordinal);
        foreach (var (name, value) in overrides)
        {
            merged[name] = value;
        }

        return merged;
    }

    [Authorize(AbpAdminPermissions.LanguageTexts.Edit)]
    public virtual async Task<LanguageTextDto> UpdateAsync(UpdateLanguageTextDto input)
    {
        // fail-closed 校验：未注册资源/非法文化不允许落库——孤儿覆盖行在跨资源列表里
        // 不可见、不可管理（宇宙来自注册表），客户端可控字符串还会进缓存 key，写入口是唯一闸门
        if (!_localizationOptions.Value.Resources.ContainsKey(input.ResourceName))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Localization.UnknownResourceName);
        }

        try
        {
            _ = CultureInfo.GetCultureInfo(input.CultureName);
        }
        catch (CultureNotFoundException)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Localization.InvalidCultureName);
        }

        var text = await _languageTextRepository.FindAsync(
            CurrentTenant.Id, input.ResourceName, input.CultureName, input.Name);

        if (text == null)
        {
            text = new LanguageText(
                GuidGenerator.Create(),
                CurrentTenant.Id,
                input.ResourceName,
                input.CultureName,
                input.Name,
                input.Value);

            await _languageTextRepository.InsertAsync(text);
        }
        else
        {
            text.SetValue(input.Value);
            await _languageTextRepository.UpdateAsync(text);
        }

        // 使缓存失效：host 侧写入时版本号替换会让所有租户的合并缓存条目一并作废。
        // 挂在 UoW 提交后执行，避免"版本已换、事务未提交"窗口里并发读按新版本 key 回填旧值。
        UnitOfWorkManager.Current!.OnCompleted(async () =>
        {
            try
            {
                await _localizationStore.InvalidateCacheAsync(input.ResourceName, input.CultureName);
            }
            catch (Exception e)
            {
                // 此时事务已提交：清缓存失败只造成最长一个 TTL 的脏读，降级记日志，不应让客户端收到 500
                Logger.LogWarning(e, "本地化缓存失效失败，Resource={Resource}, Culture={Culture}",
                    input.ResourceName, input.CultureName);
            }
        });

        return ToOverrideRowDto(text);
    }

    /// <summary>
    /// 恢复默认：只删除当前上下文（host 或本租户）的覆盖行，让管线回退提供值
    /// （租户上下文：host 覆盖或静态原文；host 上下文：静态原文）。Pro 同款 per-context 语义。
    /// 与列表 IsOverridden（当前上下文口径）必须保持同口径：前端只在 IsOverridden=true 时
    /// 放开"恢复默认"按钮。注意：删行必然发生，但若覆盖值恰好等于下层提供值，
    /// 生效值（显示值）可能不变——IsOverridden 只保证"删的是真实存在的覆盖行"。
    /// </summary>
    [Authorize(AbpAdminPermissions.LanguageTexts.Edit)]
    public virtual async Task RestoreToDefaultAsync(string resourceName, string cultureName, string name)
    {
        await _languageTextRepository.DeleteAsync(CurrentTenant.Id, resourceName, cultureName, name);

        // 使缓存失效（同 UpdateAsync：提交后执行 + 版本号对全体租户生效）
        UnitOfWorkManager.Current!.OnCompleted(async () =>
        {
            try
            {
                await _localizationStore.InvalidateCacheAsync(resourceName, cultureName);
            }
            catch (Exception e)
            {
                // 此时事务已提交：清缓存失败只造成最长一个 TTL 的脏读，降级记日志，不应让客户端收到 500
                Logger.LogWarning(e, "本地化缓存失效失败，Resource={Resource}, Culture={Culture}",
                    resourceName, cultureName);
            }
        });
    }

    /// <summary>
    /// 注册资源名列表（页面「资源」筛选下拉的数据源）。
    /// 与 GetListAsync「空资源 = 全部」用的是同一个宇宙（AbpLocalizationOptions.Resources），
    /// 两者必须保持同源：下拉里选得到的资源，跨资源列表里一定列得到，反之亦然。
    /// </summary>
    public virtual async Task<ListResultDto<string>> GetResourceNamesAsync()
    {
        var resourceNames = _localizationOptions.Value.Resources.Keys.ToList();
        return await Task.FromResult(new ListResultDto<string>(resourceNames));
    }
}
