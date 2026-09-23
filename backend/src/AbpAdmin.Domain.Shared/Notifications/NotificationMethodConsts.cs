namespace AbpAdmin.Notifications;

/// <summary>
/// 通知渠道标识常量（T3.5）。值与 T3.4 数据字典 NotificationMethod、
/// EasyAbp NotificationService 的 NotificationMethod 对齐——散落的裸字符串拼错要到运行时才暴露。
/// </summary>
public static class NotificationMethodConsts
{
    public const string Mailing = "Mailing";

    public const string Sms = "Sms";

    /// <summary>与 <see cref="InAppNotificationConsts.NotificationMethod"/> 同值，保持单一来源引用。</summary>
    public const string InApp = InAppNotificationConsts.NotificationMethod;
}
