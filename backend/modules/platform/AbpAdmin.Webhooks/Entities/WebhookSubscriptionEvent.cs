using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Webhooks;

/// <summary>
/// 订阅-事件关联（子实体）。复合主键 (SubscriptionId, EventName)：
/// 聚合内 SetEvents 做差量增删时无需代理 Guid 生成，天然防重名。
/// </summary>
public class WebhookSubscriptionEvent : Entity
{
    public virtual Guid SubscriptionId { get; protected set; }

    /// <summary>事件名（发布器的路由键，如 "identity.user.created"）。</summary>
    public virtual string EventName { get; protected set; } = default!;

    protected WebhookSubscriptionEvent() { }

    public WebhookSubscriptionEvent(Guid subscriptionId, string eventName)
    {
        SubscriptionId = subscriptionId;
        EventName = Check.NotNullOrWhiteSpace(eventName, nameof(eventName), WebhooksConsts.MaxEventNameLength);
    }

    public override object[] GetKeys() => new object[] { SubscriptionId, EventName };
}
