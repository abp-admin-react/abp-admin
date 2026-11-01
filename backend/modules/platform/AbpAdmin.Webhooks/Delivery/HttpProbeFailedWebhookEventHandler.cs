using System.Threading.Tasks;
using AbpAdmin.Monitoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EventBus;
using Volo.Abp.Uow;

namespace AbpAdmin.Webhooks.Delivery;

/// <summary>
/// 探活失败 → Webhook 桥接：订阅 <see cref="HttpProbeFailedEto"/>（进程内事件，生产方
/// HttpProbeJobHandler 在抛出前即时发布），转投 <see cref="IWebhookPublisher"/>——
/// Webhooks 模块的第一个业务事件源。订阅方按 <see cref="HttpProbeFailedEto.WebhookEventName"/>
/// （AbpAdmin.HttpProbe.Failed）建订阅即可收到。
/// 独立 requiresNew UoW：生产方发布事件后即抛异常让本次探活执行落失败记录，其 UoW 必然回滚；
/// SendRecord 与作业入队必须活下来，所以这里自己开事务提交。
/// </summary>
public class HttpProbeFailedWebhookEventHandler : ILocalEventHandler<HttpProbeFailedEto>, ITransientDependency
{
    private readonly IWebhookPublisher _webhookPublisher;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public HttpProbeFailedWebhookEventHandler(
        IWebhookPublisher webhookPublisher,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _webhookPublisher = webhookPublisher;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public virtual async Task HandleEventAsync(HttpProbeFailedEto eventData)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true);
        await _webhookPublisher.PublishAsync(HttpProbeFailedEto.WebhookEventName, new
        {
            eventData.Url,
            eventData.StatusCode,
            eventData.ErrorMessage,
            eventData.OccurredAt,
        });
        await uow.CompleteAsync();
    }
}
