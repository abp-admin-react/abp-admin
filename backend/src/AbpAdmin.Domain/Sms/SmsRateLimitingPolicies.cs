namespace AbpAdmin.Sms;

/// <summary>
/// 短信限流策略名（策略注册在 RateLimitingConfigurator.ConfigureOperationRateLimiting，
/// SmsSenderResolver 按用途二选一）。
/// round3 收敛：常量值改为引用全量常量类 <see cref="RateLimiting.OperationRateLimitingPolicyNames"/>，
/// 消除「注册处/消费处各写一份字符串」的漂移风险（头注释原写"AbpAdminDomainModule 里注册"已失实——
/// 注册逻辑早已拆到 RateLimitingConfigurator）。
/// 策略名与用途必须配套：验证码与运营通知分开限流，避免批量通知撞"同号码每小时 N 条"
/// 的验证码阈值被静默记失败（排查时策略名也会误导）。
/// </summary>
public static class SmsRateLimitingPolicies
{
    /// <summary>手机验证码：按手机号 1 小时 3 次（T2.5）。</summary>
    public const string VerificationCode = RateLimiting.OperationRateLimitingPolicyNames.SmsVerificationCode;

    /// <summary>运营/通知短信（NotificationService fan-out）：按手机号 1 小时 N 次，阈值独立配置。</summary>
    public const string Notification = RateLimiting.OperationRateLimitingPolicyNames.SmsNotification;
}

/// <summary>
/// SmsMessage.Properties 的用途标记 key。<see cref="VerificationCodeSmsMessageFactory"/> 发验证码时打标，
/// SmsSenderResolver 据此决定走哪个限流策略；未打标的消息（通知等）走 SmsNotification 策略。
/// </summary>
public static class SmsMessagePropertyKeys
{
    public const string Purpose = "Purpose";

    public const string PurposeVerificationCode = "VerificationCode";
}
