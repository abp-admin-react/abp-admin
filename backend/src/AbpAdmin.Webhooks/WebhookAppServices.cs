using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Webhooks.Delivery;
using AbpAdmin.Webhooks.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.Webhooks;

/// <summary>订阅管理实现。租户语义：订阅按 TenantId 隔离（各租户管理自己的订阅）。</summary>
[Authorize(WebhooksPermissions.Subscriptions.Default)]
public class WebhookSubscriptionAppService : ApplicationService, IWebhookSubscriptionAppService
{
    private readonly IRepository<WebhookSubscription, Guid> _subscriptionRepository;
    private readonly IRepository<WebhookSubscriptionEvent> _eventRepository;

    public WebhookSubscriptionAppService(
        IRepository<WebhookSubscription, Guid> subscriptionRepository,
        IRepository<WebhookSubscriptionEvent> eventRepository)
    {
        _subscriptionRepository = subscriptionRepository;
        _eventRepository = eventRepository;
    }

    public virtual async Task<PagedResultDto<WebhookSubscriptionDto>> GetListAsync(GetWebhookSubscriptionListInput input)
    {
        var queryable = await _subscriptionRepository.GetQueryableAsync();

        var filtered = queryable.AsQueryable();
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            filtered = filtered.Where(s =>
                s.WebhookUri.Contains(input.Filter) || (s.Description ?? string.Empty).Contains(input.Filter));
        }

        if (input.IsActive.HasValue)
        {
            filtered = filtered.Where(s => s.IsActive == input.IsActive.Value);
        }

        var ordered = filtered.OrderByDescending(s => s.CreationTime);
        var totalCount = await AsyncExecuter.CountAsync(ordered);
        var page = await AsyncExecuter.ToListAsync(
            ordered.Skip(input.SkipCount).Take(input.MaxResultCount));

        var ids = page.Select(s => s.Id).ToList();
        var eventsBySubscription = (await AsyncExecuter.ToListAsync(
                (await _eventRepository.GetQueryableAsync()).Where(e => ids.Contains(e.SubscriptionId))))
            .GroupBy(e => e.SubscriptionId)
            .ToDictionary(g => g.Key, g => g.Select(e => e.EventName).ToList());

        return new PagedResultDto<WebhookSubscriptionDto>(
            totalCount,
            page.Select(s => Map(s, eventsBySubscription.GetValueOrDefault(s.Id, new List<string>()))).ToList());
    }

    public virtual async Task<WebhookSubscriptionDto> GetAsync(Guid id)
    {
        var subscription = await _subscriptionRepository.GetAsync(id);
        var events = (await _eventRepository.GetListAsync(e => e.SubscriptionId == id))
            .Select(e => e.EventName).ToList();
        return Map(subscription, events);
    }

    [Authorize(WebhooksPermissions.Subscriptions.Create)]
    public virtual async Task<WebhookSubscriptionDto> CreateAsync(CreateWebhookSubscriptionInput input)
    {
        var subscription = new WebhookSubscription(
            GuidGenerator.Create(),
            input.WebhookUri,
            input.Secret,
            input.Events,
            input.Description,
            input.IsActive,
            tenantId: CurrentTenant.Id);

        await _subscriptionRepository.InsertAsync(subscription, autoSave: true);
        return Map(subscription, input.Events);
    }

    [Authorize(WebhooksPermissions.Subscriptions.Update)]
    public virtual async Task<WebhookSubscriptionDto> UpdateAsync(Guid id, UpdateWebhookSubscriptionInput input)
    {
        // SetEvents 做差量增删，必须带出 Events 导航（默认仓储不带 Include）
        var queryable = await _subscriptionRepository.WithDetailsAsync(s => s.Events);
        var subscription = await AsyncExecuter.FirstOrDefaultAsync(queryable.Where(s => s.Id == id))
            ?? throw new Volo.Abp.Domain.Entities.EntityNotFoundException(typeof(WebhookSubscription), id);
        subscription.SetWebhookUri(input.WebhookUri);
        // 密钥可空语义：编辑表单不回显密钥，留空 = 保持原值
        if (!string.IsNullOrWhiteSpace(input.Secret))
        {
            subscription.SetSecret(input.Secret);
        }
        subscription.SetDescription(input.Description);
        subscription.SetIsActive(input.IsActive);
        subscription.SetEvents(input.Events);

        await _subscriptionRepository.UpdateAsync(subscription, autoSave: true);
        return Map(subscription, input.Events);
    }

    [Authorize(WebhooksPermissions.Subscriptions.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        // 只停发不抹历史：SendRecord 保留（订阅删除后其记录的查看/重发按记录权限管理）
        await _subscriptionRepository.DeleteAsync(id, autoSave: true);
    }

    private static WebhookSubscriptionDto Map(WebhookSubscription subscription, List<string> events)
    {
        return new WebhookSubscriptionDto
        {
            Id = subscription.Id,
            WebhookUri = subscription.WebhookUri,
            HasSecret = !string.IsNullOrWhiteSpace(subscription.Secret),
            Description = subscription.Description,
            IsActive = subscription.IsActive,
            Events = events,
        };
    }
}

/// <summary>发送记录实现（查询/重发/删除）。</summary>
[Authorize(WebhooksPermissions.SendRecords.Default)]
public class WebhookSendRecordAppService : ApplicationService, IWebhookSendRecordAppService
{
    private readonly IRepository<WebhookSendRecord, Guid> _recordRepository;
    private readonly IBackgroundJobManager _backgroundJobManager;

    public WebhookSendRecordAppService(
        IRepository<WebhookSendRecord, Guid> recordRepository,
        IBackgroundJobManager backgroundJobManager)
    {
        _recordRepository = recordRepository;
        _backgroundJobManager = backgroundJobManager;
    }

    public virtual async Task<PagedResultDto<WebhookSendRecordDto>> GetListAsync(GetWebhookSendRecordListInput input)
    {
        var queryable = await _recordRepository.GetQueryableAsync();

        var filtered = queryable.AsQueryable();
        if (input.SubscriptionId.HasValue)
        {
            filtered = filtered.Where(r => r.SubscriptionId == input.SubscriptionId.Value);
        }

        if (!string.IsNullOrWhiteSpace(input.EventName))
        {
            filtered = filtered.Where(r => r.EventName == input.EventName);
        }

        if (input.Succeeded.HasValue)
        {
            filtered = filtered.Where(r => r.Succeeded == input.Succeeded.Value);
        }

        var ordered = filtered.OrderByDescending(r => r.CreationTime);
        var totalCount = await AsyncExecuter.CountAsync(ordered);
        var page = await AsyncExecuter.ToListAsync(
            ordered.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<WebhookSendRecordDto>(
            totalCount,
            page.Select(Map).ToList());
    }

    [Authorize(WebhooksPermissions.SendRecords.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        await _recordRepository.DeleteAsync(id, autoSave: true);
    }

    [Authorize(WebhooksPermissions.SendRecords.Resend)]
    public virtual async Task ResendAsync(Guid id)
    {
        var record = await _recordRepository.GetAsync(id);

        await _backgroundJobManager.EnqueueAsync(new WebhookDeliveryJobArgs
        {
            SubscriptionId = record.SubscriptionId,
            SendRecordId = record.Id,
            EventName = record.EventName,
            Payload = record.Payload,
            TenantId = record.TenantId,
        });
    }

    private static WebhookSendRecordDto Map(WebhookSendRecord record)
    {
        return new WebhookSendRecordDto
        {
            Id = record.Id,
            SubscriptionId = record.SubscriptionId,
            EventName = record.EventName,
            Payload = record.Payload,
            Succeeded = record.Succeeded,
            ResponseStatusCode = record.ResponseStatusCode,
            ResponseBody = record.ResponseBody,
            AttemptCount = record.AttemptCount,
            LastAttemptTime = record.LastAttemptTime,
        };
    }
}
