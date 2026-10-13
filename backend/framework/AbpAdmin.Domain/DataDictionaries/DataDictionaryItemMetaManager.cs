using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Domain.Services;

namespace AbpAdmin.DataDictionaries;

/// <summary>管理页保存字典项时的单条元数据写入载荷（见 <see cref="DataDictionaryItemMetaManager.SaveForDictionaryAsync"/>）。</summary>
public sealed record DataDictionaryItemMetaSaveItem(string ItemCode, string? TagType, int Order);

/// <summary>
/// 字典项元数据的写入入口（T3.4 第 12 步）。
/// 三个语义，按调用方分开：管理页原子保存用 <see cref="SaveForDictionaryAsync"/>
/// （用户改动必须落库，含孤儿清理）；字典整本删除用 <see cref="DeleteAllForDictionaryAsync"/>；
/// 两个种子贡献者（枚举同步、手写字典）用 <see cref="UpsertIfMissingAsync"/>——
/// 种子只负责"首次给默认值"，用户在管理页改过的 TagType/Order 不能在重新部署时被重置
/// （代价：代码里改 [DictionaryTag]/声明顺序不再自动传播，需删行重种或界面改）。
/// 孤儿元数据（对应字典项已不存在）的清理也收口在这里：管理页保存经 SaveForDictionaryAsync
/// 即时清、字典删除经 DeleteAllForDictionaryAsync 即时清；枚举种子的全局兜底扫
/// （含历史残留）是第二份实现，语义不同故保留。
/// 查询与插入都走当前租户上下文（多租户过滤器 + CurrentTenant.Id），调用方负责先切租户。
/// </summary>
public class DataDictionaryItemMetaManager : DomainService
{
    private readonly IRepository<DataDictionaryItemMeta, Guid> _metaRepository;

    public DataDictionaryItemMetaManager(IRepository<DataDictionaryItemMeta, Guid> metaRepository)
    {
        _metaRepository = metaRepository;
    }

    /// <summary>种子专用：只插缺失行，已存在的行（含用户自定义）一律不动。</summary>
    public virtual async Task UpsertIfMissingAsync(string dictionaryCode, string itemCode, string? tagType, int order)
    {
        var meta = await FindAsync(dictionaryCode, itemCode);
        if (meta == null)
        {
            await InsertAsync(dictionaryCode, itemCode, tagType, order);
        }
    }

    /// <summary>
    /// 删除字典的全部元数据行（字典本体被删除时随行清理；非静态字典删除路径调用）。
    /// </summary>
    public virtual async Task<int> DeleteAllForDictionaryAsync(string dictionaryCode)
    {
        var metas = await _metaRepository.GetListAsync(x => x.DictionaryCode == dictionaryCode);
        foreach (var meta in metas)
        {
            await _metaRepository.DeleteAsync(meta);
        }

        return metas.Count;
    }

    /// <summary>
    /// 管理页原子保存专用（用户显式改动，已存在行必须跟随落库）：一次载入该字典全部元数据行，
    /// 内存对位后更新/缺失插入，并清掉不在 <paramref name="keepItemCodes"/> 里的孤儿行。
    /// items 与 keepItemCodes 分开传：非静态字典里项级 IsStatic 的幸存项（不在提交列表但受保护）
    /// 的元数据保持原样，既不更新也不删除。一次 GetListAsync 摊平逐项 FindAsync 的 N+1
    /// （字典项是几十量级，全量载入零负担；查询命中 (TenantId, DictionaryCode, ItemCode) 唯一索引）。
    /// </summary>
    public virtual async Task SaveForDictionaryAsync(
        string dictionaryCode,
        IReadOnlyList<DataDictionaryItemMetaSaveItem> items,
        IEnumerable<string> keepItemCodes)
    {
        var metas = await _metaRepository.GetListAsync(x => x.DictionaryCode == dictionaryCode);
        var metaByItemCode = metas.ToDictionary(x => x.ItemCode, StringComparer.Ordinal);
        var keep = keepItemCodes.ToHashSet(StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (metaByItemCode.TryGetValue(item.ItemCode, out var meta))
            {
                meta.Update(item.TagType, item.Order);
            }
            else
            {
                await InsertAsync(dictionaryCode, item.ItemCode, item.TagType, item.Order);
            }
        }

        foreach (var orphan in metas.Where(m => !keep.Contains(m.ItemCode)))
        {
            await _metaRepository.DeleteAsync(orphan);
        }
    }

    private Task<DataDictionaryItemMeta?> FindAsync(string dictionaryCode, string itemCode)
    {
        return _metaRepository.FindAsync(x =>
            x.DictionaryCode == dictionaryCode && x.ItemCode == itemCode);
    }

    private async Task InsertAsync(string dictionaryCode, string itemCode, string? tagType, int order)
    {
        await _metaRepository.InsertAsync(new DataDictionaryItemMeta(
            GuidGenerator.Create(), CurrentTenant.Id, dictionaryCode, itemCode, tagType, order));
    }
}
