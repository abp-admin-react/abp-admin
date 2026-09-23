using System;
using System.Threading.Tasks;
using EasyAbp.NotificationService.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace AbpAdmin.Notifications;

/// <summary>
/// 站内信记录创建后的发送调度（T3.5），照模块的 SmsNotificationCreationEventHandler 写：
/// UoW 提交后（记录已落库）在新 scope 里入队发送作业。记录先于推送存在——
/// "推送失败不影响落库"（T3.2 第 7 步）就靠这个顺序保证。
/// </summary>
public class InAppNotificationCreationEventHandler : NotificationCreationEventHandlerBase
{
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<InAppNotificationCreationEventHandler> _logger;

    protected override string NotificationMethod => InAppNotificationConsts.NotificationMethod;

    public InAppNotificationCreationEventHandler(
        IUnitOfWorkManager unitOfWorkManager,
        IServiceScopeFactory serviceScopeFactory,
        ILogger<InAppNotificationCreationEventHandler> logger)
    {
        _unitOfWorkManager = unitOfWorkManager;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override Task InternalHandleEventAsync(EntityCreatedEventData<Notification> eventData)
    {
        // round4：与其余 6 处 OnCompleted 降级同款（round2/3 漏掉的第 7 处）——
        // 提交后回调里入队失败不能把已提交的请求炸成 500；此时站内信行已落库，
        // 丢的只是本条的实时推送与发送结果标记（收件箱仍可见），记错误日志即可。
        // Current 判空防御：实体事件理论上都在 UoW 内，但种子/后台路径若变更再触发会 NRE。
        var unitOfWork = _unitOfWorkManager.Current;
        if (unitOfWork == null)
        {
            _logger.LogWarning(
                "站内信 {NotificationId} 创建事件不在 UoW 内触发，发送作业未入队（记录已落库）。",
                eventData.Entity.Id);
            return Task.CompletedTask;
        }

        unitOfWork.OnCompleted(async () =>
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IBackgroundJobManager>()
                    .EnqueueAsync(new InAppNotificationSendingJobArgs(eventData.Entity.TenantId, eventData.Entity.Id));
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,
                    "站内信 {NotificationId} 的发送作业入队失败（记录已落库，本条实时推送丢失）。",
                    eventData.Entity.Id);
            }
        });
        return Task.CompletedTask;
    }
}

[Serializable]
public class InAppNotificationSendingJobArgs : IMultiTenant
{
    public Guid? TenantId { get; set; }

    public Guid NotificationId { get; set; }

    public InAppNotificationSendingJobArgs()
    {
    }

    public InAppNotificationSendingJobArgs(Guid? tenantId, Guid notificationId)
    {
        TenantId = tenantId;
        NotificationId = notificationId;
    }
}
