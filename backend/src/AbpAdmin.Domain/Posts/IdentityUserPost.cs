using System;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Posts;

/// <summary>
/// 用户-岗位关联（对标 RuoYi sys_user_post）。复合主键，无跨表外键：
/// UserId 逻辑引用 IdentityUser（ABP 模块实体），PostId 逻辑引用本模块 <see cref="Post"/>；
/// 岗位软删除时关联保留，由仓储查询按岗位存活状态过滤。
/// </summary>
public class IdentityUserPost : Entity, IMultiTenant
{
    public Guid UserId { get; protected set; }

    public Guid PostId { get; protected set; }

    public virtual Guid? TenantId { get; protected set; }

    protected IdentityUserPost() { }

    public IdentityUserPost(Guid userId, Guid postId, Guid? tenantId = null)
    {
        UserId = userId;
        PostId = postId;
        TenantId = tenantId;
    }

    public override object[] GetKeys()
    {
        return new object[] { UserId, PostId };
    }
}
