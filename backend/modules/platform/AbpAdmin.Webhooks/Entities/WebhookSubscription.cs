using System;
using System.Collections.Generic;
using System.Linq;
using Volo.Abp;
using Volo.Abp.Auditing;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Webhooks;

/// <summary>
/// Webhook 订阅：一个外部系统对若干事件名的投递契约（URL + 签名密钥）。
/// 事件订阅用子表（<see cref="Events"/>）而不是逗号串——"哪些订阅收事件 X"是发布路径的
/// 高频查询，必须可索引。
/// </summary>
public class WebhookSubscription : AuditedAggregateRoot<Guid>, IMultiTenant
{
    /// <summary>投递目标 URL（https 强烈建议；签名为 HMAC-SHA256，见 WebhookDeliveryJob）。</summary>
    public virtual string WebhookUri { get; protected set; } = default!;

    /// <summary>
    /// HMAC-SHA256 签名密钥（对 "{timestamp}.{body}" 签名，X-AbpAdmin-Signature 头携带）。
    /// [DisableAuditing]：实体历史（AddAllEntities）不得落密钥新旧值。
    /// </summary>
    [DisableAuditing]
    public virtual string Secret { get; protected set; } = default!;

    public virtual string? Description { get; protected set; }

    /// <summary>false = 暂停投递（发布器跳过，发送记录保留）。</summary>
    public virtual bool IsActive { get; protected set; }

    public virtual Guid? TenantId { get; protected set; }

    public virtual ICollection<WebhookSubscriptionEvent> Events { get; protected set; } = default!;

    protected WebhookSubscription() { }

    public WebhookSubscription(
        Guid id,
        string webhookUri,
        string secret,
        IEnumerable<string> events,
        string? description = null,
        bool isActive = true,
        Guid? tenantId = null)
        : base(id)
    {
        SetWebhookUri(webhookUri);
        SetSecret(secret);
        SetDescription(description);
        IsActive = isActive;
        TenantId = tenantId;
        Events = new List<WebhookSubscriptionEvent>();
        SetEvents(events);
    }

    public void SetWebhookUri(string uri)
    {
        WebhookUri = Check.NotNullOrWhiteSpace(uri, nameof(uri), WebhooksConsts.MaxWebhookUriLength);
    }

    public void SetSecret(string secret)
    {
        // 密钥短于 16 字节时 HMAC 抗碰意义仍在但暴力空间小——发布侧不做硬限制（外部系统自管），
        // 只挡空串；强度要求属订阅方 UI 引导
        Secret = Check.NotNullOrWhiteSpace(secret, nameof(secret), WebhooksConsts.MaxSecretLength);
    }

    public void SetDescription(string? description)
    {
        Description = Check.Length(description, nameof(description), WebhooksConsts.MaxDescriptionLength);
    }

    public void SetIsActive(bool isActive) => IsActive = isActive;

    /// <summary>
    /// 整体替换事件订阅集。重名 = 调用方契约错误（显式拒绝，不静默去重）；
    /// 复合键子实体的差量增删交给 EF 变更跟踪（按 EventName 比对）。
    /// </summary>
    public void SetEvents(IEnumerable<string> eventNames)
    {
        var normalized = (eventNames ?? throw new ArgumentNullException(nameof(eventNames)))
            .Select(e => Check.NotNullOrWhiteSpace(e, nameof(eventNames), WebhooksConsts.MaxEventNameLength).Trim())
            .ToList();

        var duplicated = normalized.GroupBy(e => e).FirstOrDefault(g => g.Count() > 1);
        if (duplicated != null)
        {
            throw new ArgumentException($"Duplicated event name: {duplicated.Key}");
        }

        var current = Events.ToDictionary(e => e.EventName);
        var target = normalized.ToDictionary(e => e, e => e);

        foreach (var existing in Events.Where(e => !target.ContainsKey(e.EventName)).ToList())
        {
            Events.Remove(existing);
        }

        foreach (var eventName in normalized.Where(n => !current.ContainsKey(n)))
        {
            Events.Add(new WebhookSubscriptionEvent(Id, eventName));
        }
    }

    public bool SubscribesTo(string eventName) =>
        Events.Any(e => e.EventName == eventName);
}
