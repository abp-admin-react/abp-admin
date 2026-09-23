using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Posts;

/// <summary>
/// 岗位（对标 RuoYi sys_post）。组织维度由 ABP 组织单元承担，岗位是用户的职务维度：
/// 一个用户可同时属于多个岗位。系统级配置，不挂行级数据权限（IHasDataScope）。
/// FullAudited 与菜单同款：删除走硬删（唯一索引占位问题见 AppService.DeleteAsync）。
/// </summary>
public class Post : FullAuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>岗位名称，如「董事长」「人力资源经理」。租户内唯一。</summary>
    public virtual string Name { get; protected set; } = default!;

    /// <summary>岗位编码，如 CEO、HR。租户内唯一，用户导入时按它关联。</summary>
    public virtual string Code { get; protected set; } = default!;

    /// <summary>显示顺序，越小越靠前。</summary>
    public virtual int SortOrder { get; protected set; }

    /// <summary>状态。停用后不出现在用户表单的岗位下拉里，已关联不受影响。</summary>
    public virtual PostStatusEnum Status { get; protected set; }

    public virtual string? Remark { get; protected set; }

    protected Post() { }

    public Post(
        Guid id,
        string name,
        string code,
        int sortOrder = 0,
        PostStatusEnum status = PostStatusEnum.Enabled,
        string? remark = null,
        Guid? tenantId = null)
        : base(id)
    {
        SetName(name);
        SetCode(code);
        SortOrder = sortOrder;
        Status = status;
        SetRemark(remark);
        TenantId = tenantId;
    }

    public Post SetName(string name)
    {
        Name = Check.NotNullOrWhiteSpace(name, nameof(name), maxLength: PostConsts.MaxNameLength);
        return this;
    }

    public Post SetCode(string code)
    {
        Code = Check.NotNullOrWhiteSpace(code, nameof(code), maxLength: PostConsts.MaxCodeLength);
        return this;
    }

    public Post SetSortOrder(int sortOrder)
    {
        SortOrder = sortOrder;
        return this;
    }

    public Post SetStatus(PostStatusEnum status)
    {
        Status = status;
        return this;
    }

    public Post SetRemark(string? remark)
    {
        Remark = Check.Length(remark, nameof(remark), PostConsts.MaxRemarkLength);
        return this;
    }
}
