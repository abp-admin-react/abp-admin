using System;
using System.Collections.Generic;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 数据范围不可变快照。一次请求内共享同一实例，禁止就地修改。
/// <para>不可变性的教训来自 ABP 自己的 <c>DataFilter&lt;T&gt;</c>：就地修改共享对象会跨兄弟分支泄漏。</para>
/// </summary>
public sealed class DataScopeSnapshot
{
    /// <summary>
    /// 是否拥有全部数据权限（任一角色为 <see cref="DataScopeTypeEnum.All"/>）。
    /// </summary>
    public bool IsAll { get; }

    /// <summary>
    /// 是否叠加「仅本人创建」条件（任一角色为 <see cref="DataScopeTypeEnum.SelfOnly"/>）。
    /// 与 <see cref="OrganizationUnitIds"/> 是「或」关系。
    /// </summary>
    public bool SelfOnly { get; }

    /// <summary>
    /// 可见的组织单元 Id 集合（已按所有角色范围求并集）。
    /// 空集合表示零行可见（fail-closed），除非 <see cref="IsAll"/> 为 <c>true</c>。
    /// </summary>
    public IReadOnlyCollection<Guid> OrganizationUnitIds { get; }

    /// <summary>
    /// 当前用户 Id。未认证请求为 <c>null</c>。
    /// </summary>
    public Guid? UserId { get; }

    public DataScopeSnapshot(bool isAll, bool selfOnly, IReadOnlyCollection<Guid> organizationUnitIds, Guid? userId)
    {
        IsAll = isAll;
        SelfOnly = selfOnly;
        OrganizationUnitIds = organizationUnitIds ?? Array.Empty<Guid>();
        UserId = userId;
    }
}
