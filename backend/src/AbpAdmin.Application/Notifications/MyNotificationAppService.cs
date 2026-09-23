using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using EasyAbp.NotificationService.NotificationInfos;
using EasyAbp.NotificationService.Notifications;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace AbpAdmin.Notifications;

/// <summary>
/// 用户侧"我的通知"（T3.5 第 11 步）。只标 [Authorize]，不要任何具体权限；
/// 查询强制 Where(UserId == CurrentUser.GetId())，是整个端点的安全边界。
/// 只返回站内信（InApp）——用户不需要在"我的通知"里看到"系统给你发了一条短信"。
/// </summary>
[Authorize]
public class MyNotificationAppService : ApplicationService, IMyNotificationAppService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly INotificationInfoRepository _notificationInfoRepository;
    private readonly IIdentityUserRepository _userRepository;

    public MyNotificationAppService(
        INotificationRepository notificationRepository,
        INotificationInfoRepository notificationInfoRepository,
        IIdentityUserRepository userRepository)
    {
        _notificationRepository = notificationRepository;
        _notificationInfoRepository = notificationInfoRepository;
        _userRepository = userRepository;
    }

    public virtual async Task<PagedResultDto<MyNotificationDto>> GetListAsync(GetMyNotificationsInput input)
    {
        var userId = CurrentUser.GetId();
        var lastReadTime = await GetLastReadTimeAsync(userId);

        var notificationQuery = (await _notificationRepository.GetQueryableAsync())
            .Where(n => n.UserId == userId && n.NotificationMethod == InAppNotificationConsts.NotificationMethod);

        var totalCount = await AsyncExecuter.CountAsync(notificationQuery);

        var query = from notification in notificationQuery
                    join info in await _notificationInfoRepository.GetQueryableAsync()
                        on notification.NotificationInfoId equals info.Id
                    orderby notification.CreationTime descending
                    select new { notification, info };

        var items = await AsyncExecuter.ToListAsync(query.Skip(input.SkipCount).Take(input.MaxResultCount));

        return new PagedResultDto<MyNotificationDto>(
            totalCount,
            items.Select(x => new MyNotificationDto
            {
                Id = x.notification.Id,
                Title = x.info.GetInAppTitle(),
                Body = x.info.GetInAppBody(),
                CreationTime = x.notification.CreationTime,
                IsRead = x.notification.CreationTime <= lastReadTime
            }).ToList());
    }

    public virtual async Task<UnreadCountDto> GetUnreadCountAsync()
    {
        var userId = CurrentUser.GetId();
        var lastReadTime = await GetLastReadTimeAsync(userId);

        var count = await AsyncExecuter.CountAsync(
            (await _notificationRepository.GetQueryableAsync())
                .Where(n => n.UserId == userId
                            && n.NotificationMethod == InAppNotificationConsts.NotificationMethod
                            && n.CreationTime > lastReadTime));

        return new UnreadCountDto { Count = count };
    }

    public virtual async Task MarkAllAsReadAsync()
    {
        var user = await _userRepository.GetAsync(CurrentUser.GetId());
        // 存 Ticks 而不是日期字符串（原因见 UserNotificationConsts 注释）
        user.SetProperty(UserNotificationConsts.LastReadTimePropertyName, Clock.Now.Ticks);
        await _userRepository.UpdateAsync(user);
    }

    protected virtual async Task<DateTime> GetLastReadTimeAsync(Guid userId)
    {
        var user = await _userRepository.FindAsync(userId);
        var ticks = user?.GetProperty<long?>(UserNotificationConsts.LastReadTimePropertyName);
        return ticks.HasValue ? new DateTime(ticks.Value, DateTimeKind.Unspecified) : DateTime.MinValue;
    }
}
