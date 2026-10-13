using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Gdpr;

/// <summary>
/// GDPR 个人数据请求实体。
/// 用户发起个人数据导出请求后，系统会在 ReadyTime 之后准备好数据供下载。
/// </summary>
public class GdprRequest : Entity<Guid>, IMultiTenant
{
    /// <summary>
    /// 租户 ID。个人数据按用户隔离，不按组织隔离。
    /// </summary>
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>
    /// 发起请求的用户 ID。
    /// </summary>
    public virtual Guid UserId { get; protected set; }

    /// <summary>
    /// 请求创建时间。
    /// </summary>
    public virtual DateTime CreationTime { get; protected set; }

    /// <summary>
    /// 数据准备就绪时间。计算规则：CreationTime + AbpAdminGdprOptions.MinutesForDataPreparation。
    /// 在构造函数里算好，不要在读取时算。
    /// </summary>
    public virtual DateTime ReadyTime { get; protected set; }

    protected GdprRequest()
    {
    }

    public GdprRequest(Guid id, Guid? tenantId, Guid userId, DateTime creationTime, TimeSpan preparationTime)
        : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        CreationTime = creationTime;
        ReadyTime = creationTime.Add(preparationTime);
    }
}
