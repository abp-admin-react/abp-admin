using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.RealTime;
using EasyAbp.NotificationService.NotificationInfos;
using EasyAbp.NotificationService.Notifications;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace AbpAdmin.Notifications;

/// <summary>
/// 站内信渠道的 NotificationManager（T3.5 第 7 步，方案 B：复用模块的 Notification 记录）。
/// 照着模块自带的 EmailNotificationManager / SmsNotificationManager 写（3.9.0 反编译核实基类成员：
/// 抽象属性 NotificationMethod + 抽象方法 CreateAsync / SendNotificationAsync）。
///
/// 站内信的"发送"= 通过 SignalR 推一下（T3.2 的 IRealTimeNotifier，最佳努力）。
/// 记录本身由模块的事件管线持久化；用户不在线时推送为空操作，记录视为已送达（收件箱模型），
/// 与 T3.2 第 7 步"推送失败不影响落库"一致——所以推送后直接标记成功。
/// </summary>
public class InAppNotificationManager : NotificationManagerBase, ITransientDependency
{
    protected override string NotificationMethod => InAppNotificationConsts.NotificationMethod;

    protected IRealTimeNotifier RealTimeNotifier => LazyServiceProvider.LazyGetRequiredService<IRealTimeNotifier>();

    protected IIdentityUserRepository IdentityUserRepository => LazyServiceProvider.LazyGetRequiredService<IIdentityUserRepository>();

    [UnitOfWork(true)]
    public override async Task<(List<Notification>, NotificationInfo)> CreateAsync(CreateNotificationInfoModel model)
    {
        var notificationInfo = new NotificationInfo(GuidGenerator.Create(), CurrentTenant.Id);
        notificationInfo.SetInAppData(
            model.GetProperty<string?>(InAppNotificationConsts.TitlePropertyName),
            model.GetProperty<string?>(InAppNotificationConsts.BodyPropertyName));

        return (await CreateNotificationsAsync(notificationInfo, model), notificationInfo);
    }

    [UnitOfWork]
    protected override async Task SendNotificationAsync(Notification notification, NotificationInfo notificationInfo)
    {
        var title = notificationInfo.GetInAppTitle();
        var body = notificationInfo.GetInAppBody();

        await RealTimeNotifier.NotifyUserAsync(notification.UserId, new RealTimeMessage
        {
            Name = RealTimeMessageNames.Notification,
            Payload = new
            {
                Id = notification.Id,
                Title = title,
                Body = body,
                CreationTime = notification.CreationTime
            }
        });

        // 顺带推未读数，前端铃铛直接 setQueryData，省一次 invalidate 后的 refetch
        var unreadCount = await GetUnreadCountAsync(notification.UserId);
        await RealTimeNotifier.NotifyUserAsync(notification.UserId, new RealTimeMessage
        {
            Name = RealTimeMessageNames.UnreadCount,
            Payload = new { Count = unreadCount }
        });

        await SetNotificationResultAsync(notification, true);
    }

    /// <summary>未读数 = 上次"全部已读"时间戳之后创建的站内信条数（时间戳方案，规格第 7 步）。</summary>
    protected virtual async Task<int> GetUnreadCountAsync(Guid userId)
    {
        var lastReadTime = await GetLastReadTimeAsync(userId);
        var queryable = await NotificationRepository.GetQueryableAsync();
        return await AsyncExecuter.CountAsync(System.Linq.Queryable.Where(queryable,
            n => n.UserId == userId
                 && n.NotificationMethod == InAppNotificationConsts.NotificationMethod
                 && n.CreationTime > lastReadTime));
    }

    protected virtual async Task<DateTime> GetLastReadTimeAsync(Guid userId)
    {
        var user = await IdentityUserRepository.FindAsync(userId);
        var ticks = user?.GetProperty<long?>(UserNotificationConsts.LastReadTimePropertyName);
        return ticks.HasValue ? new DateTime(ticks.Value, DateTimeKind.Unspecified) : DateTime.MinValue;
    }
}
