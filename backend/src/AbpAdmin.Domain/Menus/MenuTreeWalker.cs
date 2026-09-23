using System;
using System.Collections.Generic;

namespace AbpAdmin.Menus;

/// <summary>
/// 菜单树父链遍历帮助器。统一「沿 ParentId 上行 + 防环深度上限」的遍历结构，
/// 供可见性祖先检查（MenuVisibilityCalculator）、挂载父解析、套餐祖先保留
/// （MenuManager.FilterWithAncestors）等场景复用，防环上限统一引用
/// <see cref="MenuConsts.MaxTreeDepth"/>（避免魔法数 64 散落各处）。
/// </summary>
public static class MenuTreeWalker
{
    /// <summary>
    /// 从 <paramref name="startId"/> 出发沿父链向上的节点序列。
    /// 任一环节在 <paramref name="byId"/> 中查不到（断链/悬空 ParentId）时返回 null，表示链不完整，
    /// 由调用方按各自语义决定断链的处理（可见性检查视作不可见、挂载父解析回落根级、祖先保留跳过）。
    /// <paramref name="includeSelf"/> 决定是否包含 startId 对应节点自身（套餐过滤场景需要保留勾选节点本身）。
    /// 防环：最多上行 <see cref="MenuConsts.MaxTreeDepth"/> 步。
    /// </summary>
    public static List<Menu>? AncestorChainOf(
        IReadOnlyDictionary<Guid, Menu> byId,
        Guid startId,
        bool includeSelf = false)
    {
        if (!byId.TryGetValue(startId, out var start))
        {
            return null;
        }

        var chain = new List<Menu>();
        if (includeSelf)
        {
            chain.Add(start);
        }

        var cursor = start.ParentId;
        var guard = 0;
        while (cursor != null && guard++ < MenuConsts.MaxTreeDepth)
        {
            if (!byId.TryGetValue(cursor.Value, out var ancestor))
            {
                return null;
            }

            chain.Add(ancestor);
            cursor = ancestor.ParentId;
        }

        return chain;
    }
}
