namespace AbpAdmin.Notifications;

/// <summary>
/// 用户侧通知读取状态常量（T3.5）。
/// 未读数用"上次全部已读时间戳"实现： unread = CreationTime &gt; LastReadTime 的站内信条数。
/// 代价是不支持单条已读，只支持"全部标记已读"——这是规格第 7 步明确选择的取舍。
/// </summary>
public static class UserNotificationConsts
{
    /// <summary>
    /// 存在 IdentityUser.ExtraProperties 里的"上次全部已读时间"，值为 Clock.Now.Ticks（long）。
    /// 不要存日期字符串：ExtraProperties 的 JSON 往返会把 ISO 日期字符串规格化成
    /// InvariantCulture 格式并丢掉小数秒（T3.5 测试实测），Ticks 是精确往返的。
    /// </summary>
    public const string LastReadTimePropertyName = "AbpAdmin.NotificationLastReadTime";
}
