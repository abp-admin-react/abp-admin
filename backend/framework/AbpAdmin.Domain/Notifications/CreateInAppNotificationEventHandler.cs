using System.Threading.Tasks;
using EasyAbp.NotificationService.NotificationInfos;
using EasyAbp.NotificationService.Notifications;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Uow;

namespace AbpAdmin.Notifications;

/// <summary>
/// 站内信创建 ETO 的处理器（T3.5），照模块的 CreateSmsNotificationEventHandler 写：
/// 创建 NotificationInfo + 每个用户一条 Notification。创建后的实体事件由
/// InAppNotificationCreationEventHandler 接管（入队发送作业）。
/// </summary>
public class CreateInAppNotificationEventHandler : IDistributedEventHandler<CreateInAppNotificationEto>, ITransientDependency
{
    private readonly INotificationRepository _notificationRepository;
    private readonly INotificationInfoRepository _notificationInfoRepository;
    private readonly InAppNotificationManager _inAppNotificationManager;

    public CreateInAppNotificationEventHandler(
        INotificationRepository notificationRepository,
        INotificationInfoRepository notificationInfoRepository,
        InAppNotificationManager inAppNotificationManager)
    {
        _notificationRepository = notificationRepository;
        _notificationInfoRepository = notificationInfoRepository;
        _inAppNotificationManager = inAppNotificationManager;
    }

    [UnitOfWork(true)]
    public virtual async Task HandleEventAsync(CreateInAppNotificationEto eventData)
    {
        var (notifications, notificationInfo) = await _inAppNotificationManager.CreateAsync(eventData);

        await _notificationInfoRepository.InsertAsync(notificationInfo, true);

        // 批量一次 SaveChanges（实体创建事件仍逐条触发，语义与逐条 Insert 相同）；
        // 广播一批 500 用户时不放大成 500 次独立 SaveChanges
        await _notificationRepository.InsertManyAsync(notifications, autoSave: true);
    }
}
