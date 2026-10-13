using EasyAbp.NotificationService.NotificationInfos;

namespace AbpAdmin.Notifications;

/// <summary>
/// NotificationInfo 的站内信数据存取（T3.5），对齐模块 Provider.Mailing 的 NotificationInfoExtensions。
/// 模块的 NotificationInfo 除 TenantId 外没有业务字段，标题/正文都在 ExtraProperties 里。
/// </summary>
public static class NotificationInfoInAppExtensions
{
    public static void SetInAppData(this NotificationInfo notificationInfo, string? title, string? body)
    {
        notificationInfo.SetDataValue(InAppNotificationConsts.TitlePropertyName, title);
        notificationInfo.SetDataValue(InAppNotificationConsts.BodyPropertyName, body);
    }

    public static string? GetInAppTitle(this NotificationInfo notificationInfo)
    {
        return notificationInfo.GetDataValue(InAppNotificationConsts.TitlePropertyName) as string;
    }

    public static string? GetInAppBody(this NotificationInfo notificationInfo)
    {
        return notificationInfo.GetDataValue(InAppNotificationConsts.BodyPropertyName) as string;
    }
}
