namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流策略名常量（全量 12 个，与 RateLimitingConfigurator 的 AddPolicy 一一对应）。
/// 收敛动机（round3 review）：此前策略名以裸字符串散落在 Configurator、[OperationRateLimiting] 特性
/// 与 CheckAsync/ResetAsync 直调点三处，改名时极易漏改导致 CheckAsync 抛「策略未注册」或特性静默失效。
/// 注册处（RateLimitingConfigurator）与消费点必须都引用本类；新增策略时先加常量再注册。
/// 注意：短信侧另有 SmsRateLimitingPolicies（SmsSenderResolver 按用途选择），其常量已改为引用本类，勿再写字面量。
/// </summary>
public static class OperationRateLimitingPolicyNames
{
    /// <summary>密码登录：按 IP + 按邮箱双规则（防跨账户 password spraying，消费者在 Login 页面）。</summary>
    public const string Login = "Login";

    /// <summary>短信验证码发送侧：按手机号（租户隔离）。</summary>
    public const string SmsVerificationCode = "SmsVerificationCode";

    /// <summary>运营/通知短信：按手机号（租户隔离）。</summary>
    public const string SmsNotification = "SmsNotification";

    /// <summary>邮件验证码发送侧：按邮箱 + 按 IP 双规则。</summary>
    public const string EmailVerificationCode = "EmailVerificationCode";

    /// <summary>邮件验证码校验侧（防暴力猜码）：按邮箱 + 按 IP 双规则。</summary>
    public const string EmailVerificationCodeVerify = "EmailVerificationCodeVerify";

    /// <summary>短信验证码校验侧（防暴力猜码）：按手机号 + 按 IP 双规则。</summary>
    public const string SmsVerificationCodeVerify = "SmsVerificationCodeVerify";

    /// <summary>双因素验证码发送侧：按用户 Id 参数分区（租户隔离）。</summary>
    public const string TwoFactorCode = "TwoFactorCode";

    /// <summary>双因素验证码校验侧：按用户 Id 参数分区（租户隔离）。</summary>
    public const string TwoFactorCodeVerify = "TwoFactorCodeVerify";

    /// <summary>审计日志导出：按当前用户（租户隔离）。</summary>
    public const string AuditLogExport = "AuditLogExport";

    /// <summary>匿名分享下载：按 IP（token 256-bit 随机，防脚本高频打有效 token）。</summary>
    public const string FileShareDownload = "FileShareDownload";

    /// <summary>GDPR 匿名个人数据下载：按 IP（token 单次消费凭据）。</summary>
    public const string GdprDownload = "GdprDownload";

    /// <summary>匿名图形验证码取图：按 IP（防刷 Skia 渲染与分布式缓存写入，round4）。</summary>
    public const string CaptchaImage = "CaptchaImage";
}
