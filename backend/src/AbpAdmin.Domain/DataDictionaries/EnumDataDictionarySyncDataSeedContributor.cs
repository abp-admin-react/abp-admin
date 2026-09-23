using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using EasyAbp.Abp.DataDictionary;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Uow;

namespace AbpAdmin.DataDictionaries;

/// <summary>
/// 把 C# 枚举自动同步为数据字典（T3.4 第 11 步）。
/// 放在 IDataSeedContributor 而不是 BackgroundWorker：枚举随代码发布变化，同步只需部署时跑一次，
/// DbMigrator 每次部署都跑、且 IDataSeeder 本来就逐租户调用（模块没有租户回落，字典必须逐租户写），
/// 时机与租户遍历都白拿。新建租户由 AbpAdminDatabaseMigrationEventHandler 补跑。
///
/// 规则（细节见 04-batch3-enhancements.md T3.4）：
/// - 只扫我们自己的两个程序集（Domain.Shared / Application.Contracts），不扫全部已加载程序集。
/// - 公开枚举必须以 Enum 结尾；字典 Code 取去后缀名；字典项 Code 是成员数值字符串、
///   DisplayText 取 [Description]、Description 记成员名；字典与字典项都是 IsStatic = true。
/// - 清理：删了成员的同步后对应字典项消失；整个枚举类型删掉后字典本体消失；孤儿元数据一并清掉。
/// - 幂等：同一套代码跑两次，数据库行数与内容完全不变（有测试兜底）。
/// </summary>
public class EnumDataDictionarySyncDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    /// <summary>
    /// 扫描范围限定我们自己的两个程序集（规格 04 T3.4 第 11 步）。
    /// Application.Contracts 在 Domain 层的编译期不可见（依赖方向是 Contracts → Domain.Shared），
    /// 所以按程序集名加载——DbMigrator 与 Host 的模块图都包含它，运行时一定在输出目录里。
    /// </summary>
    private static readonly string[] ScannedAssemblyNames =
    [
        "AbpAdmin.Domain.Shared",
        "AbpAdmin.Application.Contracts"
    ];

    private readonly IDataDictionaryRepository _dataDictionaryRepository;
    private readonly IDataDictionaryManager _dataDictionaryManager;
    private readonly IRepository<DataDictionaryItemMeta, Guid> _metaRepository;
    private readonly DataDictionaryItemMetaManager _metaManager;

    public ILogger<EnumDataDictionarySyncDataSeedContributor> Logger { get; set; }

    public EnumDataDictionarySyncDataSeedContributor(
        IDataDictionaryRepository dataDictionaryRepository,
        IDataDictionaryManager dataDictionaryManager,
        IRepository<DataDictionaryItemMeta, Guid> metaRepository,
        DataDictionaryItemMetaManager metaManager)
    {
        _dataDictionaryRepository = dataDictionaryRepository;
        _dataDictionaryManager = dataDictionaryManager;
        _metaRepository = metaRepository;
        _metaManager = metaManager;
        Logger = NullLogger<EnumDataDictionarySyncDataSeedContributor>.Instance;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        var plans = EnumDictionarySyncPlanBuilder.BuildPlans(
            GetCandidateTypes(), message => Logger.LogWarning("{Message}", message));

        if (plans.Count == 0)
        {
            Logger.LogWarning("枚举同步没有扫到任何以 {Suffix} 结尾的公开枚举，请检查扫描范围。", EnumDictionarySyncPlanBuilder.EnumTypeSuffix);
        }

        // 当前租户上下文下的全部字典（多租户过滤器保证只见本租户）。
        // 模块的 EF 仓储覆写了 WithDetailsAsync 以 Include Items。
        var dictionaries = await _dataDictionaryRepository.GetListAsync(includeDetails: true);

        dictionaries = await RemoveDroppedEnumDictionariesAsync(dictionaries, plans);
        await ApplyPlansAsync(dictionaries, plans, context.TenantId);
        var orphanCount = await RemoveOrphanMetasAsync(dictionaries);

        Logger.LogInformation("枚举字典同步完成：{Count} 个枚举字典，清理孤儿元数据 {OrphanCount} 行。", plans.Count, orphanCount);
    }

    /// <summary>
    /// 删除已从代码里消失的枚举字典：静态字典 = 手写集合 ∪ 枚举同步集合，两边都不在的
    /// 静态字典只可能是「枚举类型被整体删掉」或直改数据的残留（API 创建字典已强制非静态，
    /// 造不出这种行；真出现就一并清掉——静态字典的权威来源就是代码）。
    /// </summary>
    private async Task<List<DataDictionary>> RemoveDroppedEnumDictionariesAsync(
        List<DataDictionary> dictionaries, List<EnumDictionarySyncPlan> plans)
    {
        var planCodes = plans.Select(p => p.DictionaryCode).ToHashSet();
        var handWrittenCodes = AbpAdminDictionaryCodes.All.ToHashSet();

        foreach (var dict in dictionaries
                     .Where(d => d.IsStatic && !handWrittenCodes.Contains(d.Code) && !planCodes.Contains(d.Code))
                     .ToList())
        {
            Logger.LogInformation("字典 {Code} 是静态字典但既非手写种子也无对应枚举，删除（对应枚举类型已从代码移除）。", dict.Code);
            await _dataDictionaryRepository.DeleteAsync(dict);
            dictionaries.Remove(dict);
        }

        return dictionaries;
    }

    /// <summary>
    /// 逐计划 upsert 字典本体、字典项与项元数据；返回（新增/更新后的）字典集合。
    /// 已存在的字典/字典项只补缺失，不改内容：种子是"首次物化"，用户在管理页改过的
    /// 显示名与 TagType/Order 不能在重新部署时被 [Description]/[DictionaryTag] 重置
    /// （代价：代码侧改枚举文案/颜色不再自动传播，需删行重种或界面改，与 Pro 静态种子
    /// "只插入缺失项"的哲学一致）。结构同步（删除已消失的字典/项）照旧全量执行。
    /// </summary>
    private async Task ApplyPlansAsync(
        List<DataDictionary> dictionaries, List<EnumDictionarySyncPlan> plans, Guid? tenantId)
    {
        foreach (var plan in plans)
        {
            var dict = dictionaries.FirstOrDefault(d => d.Code == plan.DictionaryCode);
            var isNew = dict == null;
            // 只补缺语义下的写库守卫：项集合没变（没有新增/删除）就不调 UpdateAsync，与手写种子口径一致
            var itemCodesBefore = dict?.Items.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);

            if (isNew)
            {
                dict = new DataDictionary(
                    EnumDictionarySyncPlanBuilder.CreateStableDictionaryId(tenantId, plan.EnumType.FullName!),
                    tenantId,
                    plan.DictionaryCode,
                    plan.DictionaryDisplayText,
                    // 字典 Description 记枚举类型全名，方便排障时从字典反查代码
                    plan.EnumType.FullName!,
                    new List<DataDictionaryItem>(),
                    isStatic: true);
            }

            foreach (var item in plan.Items)
            {
                // 只插缺失项：已存在项的 DisplayText/Description 保持用户可能改过的样子
                if (dict!.Items.All(x => x.Code != item.Code))
                {
                    dict.Items.Add(new DataDictionaryItem(
                        dict.Id, tenantId, item.Code, item.DisplayText, item.Description, isStatic: true));
                }

                // 元数据补缺对已存在项也执行（UpsertIfMissingAsync 只插缺失行）：
                // 历史数据缺 meta 行（部分恢复/手工误删/旧版本种子失败）时重跑种子自愈，
                // 与手写字典种子 DataDictionaryDataSeedContributor 的口径一致
                await _metaManager.UpsertIfMissingAsync(plan.DictionaryCode, item.Code, item.TagType, item.Order);
            }

            // 清理已从代码中删除的枚举项。与模块 UpdateAsync 同款写法
            // （Items 的 getter 返回可变 List<>），不算越过聚合边界。
            // 只清理属于该枚举字典的项，别的字典不碰。
            var currentCodes = plan.Items.Select(x => x.Code).ToHashSet();
            dict!.Items.RemoveAll(x => !currentCodes.Contains(x.Code));

            if (isNew)
            {
                await _dataDictionaryManager.CreateAsync(dict);
                dictionaries.Add(dict);
            }
            else if (!itemCodesBefore!.SetEquals(dict!.Items.Select(x => x.Code)))
            {
                await _dataDictionaryManager.UpdateAsync(dict);
            }
        }
    }

    /// <summary>
    /// 清理孤儿元数据：对应字典项已不存在的 meta 行（枚举删成员/删类型、管理员删字典项后都会产生）。
    /// 保留集合用内存状态算——本 UoW 里未提交的删除对查询不可见，重新查库会把刚删的又算进去。
    /// </summary>
    private async Task<int> RemoveOrphanMetasAsync(List<DataDictionary> dictionaries)
    {
        var keep = dictionaries
            .SelectMany(d => d.Items.Select(i => (DictionaryCode: d.Code, ItemCode: i.Code)))
            .ToHashSet();
        var allMetas = await _metaRepository.GetListAsync();
        var orphans = allMetas
            .Where(m => !keep.Contains((m.DictionaryCode, m.ItemCode)))
            .ToList();
        if (orphans.Count > 0)
        {
            await _metaRepository.DeleteManyAsync(orphans);
        }

        return orphans.Count;
    }

    private IEnumerable<Type> GetCandidateTypes()
    {
        foreach (var assemblyName in ScannedAssemblyNames)
        {
            // 按名加载失败说明模块图里没这个程序集，返回空比抛异常好——
            // 上层会用「扫到 0 个枚举」的 Warning 暴露问题。
            Assembly? assembly = null;
            try
            {
                assembly = Assembly.Load(new AssemblyName(assemblyName));
            }
            catch (Exception exception)
            {
                // 按名加载失败说明模块图里没这个程序集，返回空比抛异常好；但要留下痕迹——
                // 若 Domain.Shared 仍能产出枚举而本程序集静默丢失，其枚举字典会悄悄停更
                // （字典与 ItemMeta 行缺失），只有 per-assembly 告警能暴露这类部分失效。
                Logger.LogWarning(exception,
                    "枚举字典同步按名加载程序集 {AssemblyName} 失败，该程序集的枚举本轮不会同步。", assemblyName);
            }

            if (assembly == null)
            {
                continue;
            }

            foreach (var type in assembly.GetTypes())
            {
                yield return type;
            }
        }
    }
}
