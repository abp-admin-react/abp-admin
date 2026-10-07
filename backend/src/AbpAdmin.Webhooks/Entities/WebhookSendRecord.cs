using System;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Webhooks;

/// <summary>
/// 一次交付的最终结果（含重试后的终态）。中间尝试不逐次落库——重发链路看 AttemptCount；
/// 失败排查看 ResponseStatusCode/ResponseBody（截断到常量上限）。
/// </summary>
public class WebhookSendRecord : AuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>目标订阅。订阅被删时记录保留（订阅删除只停发，不抹历史——审查/排障需要）。</summary>
    public virtual Guid SubscriptionId { get; protected set; }

    public virtual string EventName { get; protected set; } = default!;

    /// <summary>投递的请求体原文（JSON）。含业务数据，仅持 Webhooks 管理权限者可见。</summary>
    public virtual string Payload { get; protected set; } = default!;

    public virtual bool Succeeded { get; protected set; }

    /// <summary>终态 HTTP 状态码；网络层失败（未拿到响应）为 null。</summary>
    public virtual int? ResponseStatusCode { get; protected set; }

    public virtual string? ResponseBody { get; protected set; }

    /// <summary>实际尝试次数（首投 + 重试）。</summary>
    public virtual int AttemptCount { get; protected set; }

    public virtual DateTime LastAttemptTime { get; protected set; }

    public virtual Guid? TenantId { get; protected set; }

    protected WebhookSendRecord() { }

    public WebhookSendRecord(
        Guid id,
        Guid subscriptionId,
        string eventName,
        string payload,
        DateTime lastAttemptTime,
        Guid? tenantId = null)
        : base(id)
    {
        SubscriptionId = subscriptionId;
        EventName = eventName;
        Payload = payload;
        TenantId = tenantId;
        Succeeded = false;
        AttemptCount = 0;
        LastAttemptTime = lastAttemptTime;
    }

    /// <summary>交付终态落库（成功/最终失败共用，按最后一次尝试记状态）。时钟由调用方传入（实体不持有服务）。</summary>
    public void SetResult(bool succeeded, int? statusCode, string? responseBody, int attemptCount, DateTime lastAttemptTime)
    {
        Succeeded = succeeded;
        ResponseStatusCode = statusCode;
        ResponseBody = responseBody == null
            ? null
            : responseBody.Length <= AbpAdminWebhooksConsts.MaxResponseBodyLength
                ? responseBody
                : responseBody[..AbpAdminWebhooksConsts.MaxResponseBodyLength];
        AttemptCount = attemptCount;
        LastAttemptTime = lastAttemptTime;
    }
}
