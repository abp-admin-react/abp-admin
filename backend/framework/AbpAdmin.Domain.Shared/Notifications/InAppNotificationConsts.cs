namespace AbpAdmin.Notifications;

/// <summary>
/// 站内信渠道常量（T3.5）。渠道名与 T3.4 数据字典 NotificationMethod 的值对齐。
/// </summary>
public static class InAppNotificationConsts
{
    /// <summary>站内信渠道标识（EasyAbp NotificationService 的 NotificationMethod）。</summary>
    public const string NotificationMethod = "InApp";

    /// <summary>NotificationInfo.ExtraProperties 里标题的 key。</summary>
    public const string TitlePropertyName = "Title";

    /// <summary>NotificationInfo.ExtraProperties 里正文的 key。</summary>
    public const string BodyPropertyName = "Body";
}
