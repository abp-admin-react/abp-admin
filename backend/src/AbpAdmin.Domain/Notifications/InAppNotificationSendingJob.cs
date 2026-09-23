using System.Collections.Generic;
using System.Threading.Tasks;
using EasyAbp.NotificationService.NotificationInfos;
using EasyAbp.NotificationService.Notifications;
using Volo.Abp.BackgroundJobs;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace AbpAdmin.Notifications;

/// <summary>
/// 站内信发送作业（T3.5），照模块的 SmsNotificationSendingJob 写。
/// </summary>
public class InAppNotificationSendingJob : IAsyncBackgroundJob<InAppNotificationSendingJobArgs>, ITransientDependency
{
    private readonly InAppNotificationManager _inAppNotificationManager;
    private readonly ICurrentTenant _currentTenant;
    private readonly INotificationInfoRepository _notificationInfoRepository;
    private readonly INotificationRepository _notificationRepository;

    public InAppNotificationSendingJob(
        InAppNotificationManager inAppNotificationManager,
        ICurrentTenant currentTenant,
        INotificationInfoRepository notificationInfoRepository,
        INotificationRepository notificationRepository)
    {
        _inAppNotificationManager = inAppNotificationManager;
        _currentTenant = currentTenant;
        _notificationInfoRepository = notificationInfoRepository;
        _notificationRepository = notificationRepository;
    }

    [UnitOfWork]
    public virtual async Task ExecuteAsync(InAppNotificationSendingJobArgs args)
    {
        using (_currentTenant.Change(args.TenantId))
        {
            var notification = await _notificationRepository.GetAsync(args.NotificationId);
            var notificationInfo = await _notificationInfoRepository.GetAsync(notification.NotificationInfoId);

            await _inAppNotificationManager.SendNotificationsAsync(
                new List<Notification> { notification }, notificationInfo);
        }
    }
}
