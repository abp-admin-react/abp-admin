using System;
using System.Collections.Generic;
using System.Linq;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 数据范围缓存项。只在缓存读写这一层出现。
/// 形状由序列化器决定：公开无参构造 + 可写属性。
/// <para>改了下面任何一个属性（增删、改名、换类型）就必须把 <see cref="SchemaVersion"/> +1。</para>
/// </summary>
public class DataScopeCacheItem
{
    /// <summary>
    /// 缓存项 schema 版本。改了字段形状就必须递增，否则滚动发布期间新代码会读到旧 JSON 并抛反序列化异常。
    /// const 不会被序列化（System.Text.Json 只看实例属性），放这里纯粹是为了让改字段的人一眼看到它。
    /// </summary>
    public const int SchemaVersion = 1;

    public bool IsAll { get; set; }

    public bool SelfOnly { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>
    /// 用 <see cref="List{T}"/> 而不是 <see cref="IReadOnlyCollection{T}"/>，
    /// 让类型名替我们说出「这一侧是可变的」。
    /// </summary>
    public List<Guid> OrganizationUnitIds { get; set; } = new();

    public DataScopeCacheItem()
    {
    }

    public DataScopeSnapshot ToSnapshot()
    {
        return new DataScopeSnapshot(
            IsAll,
            SelfOnly,
            // 反序列化能把它填成 null（JSON 里写的是 null，或这个字段是后加的）。
            // 兜成空集合走 fail-closed，不要把 null 往筛选器那边传
            OrganizationUnitIds ?? new List<Guid>(),
            UserId);
    }

    public static DataScopeCacheItem FromSnapshot(DataScopeSnapshot snapshot)
    {
        return new DataScopeCacheItem
        {
            IsAll = snapshot.IsAll,
            SelfOnly = snapshot.SelfOnly,
            UserId = snapshot.UserId,
            OrganizationUnitIds = snapshot.OrganizationUnitIds.ToList()
        };
    }
}
