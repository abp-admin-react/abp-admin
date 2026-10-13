using System;
using Volo.Abp.Auditing;

namespace AbpAdmin.DataScopes;

/// <summary>
/// 行级数据权限标记接口。业务实体实现该接口即自动纳入数据范围过滤。
///
/// 继承 <see cref="IMayHaveCreator"/> 是为了让编译器强制带 <c>CreatorId</c>：
/// 筛选器 <c>SelfOnly</c> 分支读 <see cref="IMayHaveCreator.CreatorId"/>，
/// 实体若只实现 <see cref="IHasDataScope"/> 而不带创建者字段，
/// 会在运行期对所有人隐身或对所有人可见，且不报错。
/// <see cref="Volo.Abp.Domain.Entities.Auditing.AuditedAggregateRoot"/> 系列已经实现
/// <see cref="IMayHaveCreator"/>，演示实体与后续业务实体都走这条基类即可。
///
/// 语义定死：
/// - <see cref="OrganizationUnitId"/> 为 <c>null</c> 的行只对 <c>IsAll</c> 可见（fail-closed）
/// - <c>SelfOnly</c> 与机构范围是「或」关系
/// - <c>RoleDataScope</c>、<c>OrganizationUnit</c>、<c>IdentityUser</c> 绝不能实现本接口
///   （它们是解析器自己的输入，否则死锁）
/// </summary>
public interface IHasDataScope : IMayHaveCreator
{
    /// <summary>
    /// 该行数据所属的组织单元。<c>null</c> 表示「不属于任何组织」，
    /// 只对 <c>IsAll</c> 可见。
    /// </summary>
    Guid? OrganizationUnitId { get; }
}
