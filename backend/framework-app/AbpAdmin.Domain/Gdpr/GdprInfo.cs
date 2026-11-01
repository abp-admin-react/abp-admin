using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Gdpr;

/// <summary>
/// GDPR 个人数据信息实体。
/// 存储单个模块贡献的 JSON payload，每个模块的数据是一条记录。
/// </summary>
public class GdprInfo : AuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>
    /// 租户 ID。通过 RequestId 关联，也带上 TenantId 便于查询。
    /// </summary>
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>
    /// 关联的 GDPR 请求 ID。
    /// </summary>
    public virtual Guid RequestId { get; protected set; }

    /// <summary>
    /// 数据内容，JSON 格式的 payload。
    /// </summary>
    public virtual string Data { get; protected set; } = default!;

    /// <summary>
    /// 贡献者标识，如 "Identity"、"AuditLogging"。最长 128 字符。
    /// </summary>
    public virtual string Provider { get; protected set; } = default!;

    protected GdprInfo()
    {
    }

    public GdprInfo(Guid id, Guid? tenantId, Guid requestId, string provider, string data)
        : base(id)
    {
        TenantId = tenantId;
        RequestId = requestId;
        Provider = provider;
        Data = data;
    }
}
