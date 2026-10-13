using System;
using System.Collections.Generic;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 当前请求的数据范围状态。由 <c>DataScopeMiddleware</c> 在请求开始时写入，
/// 由 DbContext 全局筛选器在查询时读取。
/// <para>本接口不带任何生命周期标记接口——生命周期是实现的属性，不是契约的一部分。
/// 与 <c>ICurrentTenant</c> 同形。</para>
/// </summary>
public interface ICurrentDataScopeState
{
    /// <summary>
    /// 是否拥有全部数据权限。
    /// </summary>
    bool IsAll { get; }

    /// <summary>
    /// 是否叠加「仅本人创建」条件。
    /// </summary>
    bool SelfOnly { get; }

    /// <summary>
    /// 当前用户 Id。
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// 可见的组织单元 Id 集合。
    /// </summary>
    IReadOnlyCollection<Guid> OrganizationUnitIds { get; }

    /// <summary>
    /// 替换当前快照，返回 <see cref="IDisposable"/> 用于在作用域结束时还原。
    /// 形状照抄 <c>CurrentTenant.Change</c>。
    /// </summary>
    IDisposable Change(DataScopeSnapshot? snapshot);
}
