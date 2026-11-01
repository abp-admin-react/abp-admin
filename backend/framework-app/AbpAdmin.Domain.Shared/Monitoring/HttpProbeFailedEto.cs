using System;

namespace AbpAdmin.Monitoring;

/// <summary>
/// HTTP 探活失败事件（进程内 <c>ILocalEventBus</c>，发布方用 onUowComplete:false 即时投递）。
/// 生产方是 Application 层的 HttpProbeJobHandler（发布后随即抛异常让调度器落失败执行记录）；
/// 消费方在 AbpAdmin.Webhooks（桥接 IWebhookPublisher）。事件类型放 Domain.Shared：
/// Application 直引，Webhooks 经 EntityFrameworkCore → Domain 传递可见，两边不新增工程引用。
/// </summary>
public class HttpProbeFailedEto
{
    /// <summary>投递到 Webhook 订阅时的事件名（SendRecord.EventName 原样落库，订阅方按此订阅）。</summary>
    public const string WebhookEventName = "AbpAdmin.HttpProbe.Failed";

    /// <summary>探活目标地址。</summary>
    public string Url { get; set; } = default!;

    /// <summary>HTTP 状态码；null 表示未拿到响应（网络层失败）。</summary>
    public int? StatusCode { get; set; }

    /// <summary>失败摘要（异常消息/意外状态码说明，已截断）。</summary>
    public string? ErrorMessage { get; set; }

    public DateTime OccurredAt { get; set; }
}
