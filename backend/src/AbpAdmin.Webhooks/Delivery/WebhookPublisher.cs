using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Json;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace AbpAdmin.Webhooks.Delivery;

/// <summary>投递作业参数（经后台作业队列序列化，保持 POCO）。</summary>
public class WebhookDeliveryJobArgs
{
    public Guid SubscriptionId { get; set; }

    public Guid SendRecordId { get; set; }

    public string EventName { get; set; } = default!;

    /// <summary>投递的请求体原文（JSON，与 SendRecord.Payload 一致）。</summary>
    public string Payload { get; set; } = default!;

    /// <summary>订阅方的租户（作业执行时 Change 租户上下文，保证 SendRecord 落对租户）。</summary>
    public Guid? TenantId { get; set; }
}

/// <summary>
/// 交付负载信封构造（发布侧与作业侧共用同一 JSON 形态的单一出处）。
/// </summary>
public static class WebhookPayloadBuilder
{
    public static string Build(Guid eventId, string eventName, object? data, DateTime occurredAt, IJsonSerializer serializer)
    {
        return serializer.Serialize(new WebhookPayloadEnvelope
        {
            EventId = eventId,
            EventName = eventName,
            OccurredAt = occurredAt,
            Data = data,
        });
    }
}

/// <summary>投递负载信封（收端按 eventName + data 解析；签名覆盖整个请求体）。</summary>
public class WebhookPayloadEnvelope
{
    public Guid EventId { get; set; }

    public string EventName { get; set; } = default!;

    public DateTime OccurredAt { get; set; }

    public object? Data { get; set; }
}

/// <summary>
/// Webhook 发布器：把一次事件投递给所有订阅了该事件、且处于活跃状态的订阅。
/// 同步部分只做"选订阅 + 建发送记录(pending) + 入队"，HTTP 外呼在后台作业里执行
/// （不占用请求线程；宿主用 Quartz 承载作业队列）。租户语义：以当前租户上下文投递。
/// </summary>
public class WebhookPublisher : IWebhookPublisher, ITransientDependency
{
    private readonly IRepository<WebhookSubscriptionEvent> _subscriptionEventRepository;
    private readonly IRepository<WebhookSubscription, Guid> _subscriptionRepository;
    private readonly IRepository<WebhookSendRecord, Guid> _sendRecordRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly IClock _clock;
    private readonly ICurrentTenant _currentTenant;

    public WebhookPublisher(
        IRepository<WebhookSubscriptionEvent> subscriptionEventRepository,
        IRepository<WebhookSubscription, Guid> subscriptionRepository,
        IRepository<WebhookSendRecord, Guid> sendRecordRepository,
        IBackgroundJobManager backgroundJobManager,
        IGuidGenerator guidGenerator,
        IJsonSerializer jsonSerializer,
        IClock clock,
        ICurrentTenant currentTenant)
    {
        _subscriptionEventRepository = subscriptionEventRepository;
        _subscriptionRepository = subscriptionRepository;
        _sendRecordRepository = sendRecordRepository;
        _backgroundJobManager = backgroundJobManager;
        _guidGenerator = guidGenerator;
        _jsonSerializer = jsonSerializer;
        _clock = clock;
        _currentTenant = currentTenant;
    }

    public virtual async Task PublishAsync(string eventName, object? data)
    {
        var occurredAt = _clock.Now;
        var now = occurredAt;

        // 订阅筛选在数据库侧：事件名命中 + 订阅活跃
        var eventQueryable = await _subscriptionEventRepository.GetQueryableAsync();
        var subscriptionQueryable = await _subscriptionRepository.GetQueryableAsync();

        var subscriberIds = await EfExtensions.ToListAsync(
            from e in eventQueryable
            join s in subscriptionQueryable on e.SubscriptionId equals s.Id
            where e.EventName == eventName && s.IsActive
            select s.Id);

        if (subscriberIds.Count == 0)
        {
            return;
        }

        var eventId = _guidGenerator.Create();
        var payload = WebhookPayloadBuilder.Build(eventId, eventName, data, occurredAt, _jsonSerializer);

        foreach (var subscriptionId in subscriberIds)
        {
            var sendRecord = new WebhookSendRecord(
                _guidGenerator.Create(),
                subscriptionId,
                eventName,
                payload,
                now,
                tenantId: _currentTenant.Id);
            await _sendRecordRepository.InsertAsync(sendRecord);

            await _backgroundJobManager.EnqueueAsync(new WebhookDeliveryJobArgs
            {
                SubscriptionId = subscriptionId,
                SendRecordId = sendRecord.Id,
                EventName = eventName,
                Payload = payload,
                TenantId = _currentTenant.Id,
            });
        }
    }
}

/// <summary>发布入口（宿主业务与集成层调用）。</summary>
public interface IWebhookPublisher
{
    Task PublishAsync(string eventName, object? data);
}

/// <summary>模块内 EF 异步辅助（发布器不是 ApplicationService，没有 AsyncExecuter 属性）。</summary>
internal static class EfExtensions
{
    public static System.Threading.Tasks.Task<List<TSource>> ToListAsync<TSource>(this IQueryable<TSource> source) =>
        Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(source);
}
