namespace AbpAdmin.RateLimiting;

/// <summary>
/// 操作限流策略参数（问题7 修复）：
/// 原先 14 个 GetValue 硬编码默认值散在 AbpAdminDomainModule 里，与 appsettings.json 双轨漂移
/// （多数策略的键在 json 里根本不存在）。默认值集中到本类（属性初始化器），
/// 模块用 configuration.GetSection(SectionName).Get&lt;T&gt;() 一次绑定，appsettings.json 显式列出全部键。
/// 注意：属性名与 appsettings 键名一一对应（绑定不区分大小写），改键名要两边同步。
/// </summary>
public class OperationRateLimitingSettings
{
    public const string SectionName = "OperationRateLimiting";

    /// <summary>
    /// 全局开关（问题：注释曾声称可经配置覆盖、实际硬编码 true）。默认 true；
    /// 绑定 "OperationRateLimiting:IsEnabled" 键，false 时 Checker 三入口全部直接放行。
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>登录策略：按 IP + 按邮箱双规则。</summary>
    public LoginPolicySettings Login { get; set; } = new();

    /// <summary>短信验证码发送侧：按手机号（租户隔离）。</summary>
    public SmsVerificationCodePolicySettings SmsVerificationCode { get; set; } = new();

    /// <summary>运营短信（通知 fan-out）：按手机号（租户隔离）。</summary>
    public SmsNotificationPolicySettings SmsNotification { get; set; } = new();

    /// <summary>邮件验证码发送侧：按邮箱 + 按 IP 双规则（租户隔离仅邮箱规则）。</summary>
    public EmailVerificationCodePolicySettings EmailVerificationCode { get; set; } = new();

    /// <summary>验证码校验侧（防暴力猜码，邮件/短信共用参数）：按邮箱/手机号 + 按 IP 双规则。</summary>
    public VerificationCodeVerifyPolicySettings VerificationCodeVerify { get; set; } = new();

    /// <summary>双因素验证码：按用户 Id 参数分区（租户隔离）。</summary>
    public TwoFactorCodePolicySettings TwoFactorCode { get; set; } = new();

    /// <summary>审计日志导出：按当前用户（租户隔离）。</summary>
    public AuditLogExportPolicySettings AuditLogExport { get; set; } = new();

    /// <summary>匿名分享下载：按 IP 放宽限流。</summary>
    public FileShareDownloadPolicySettings FileShareDownload { get; set; } = new();

    /// <summary>GDPR 匿名个人数据下载：按 IP（token 一次性凭据，防脚本高频打下载端点）。</summary>
    public GdprDownloadPolicySettings GdprDownload { get; set; } = new();

    /// <summary>匿名图形验证码取图：按 IP（防匿名端点刷 Skia 渲染 CPU 与分布式缓存写入）。</summary>
    public CaptchaImagePolicySettings CaptchaImage { get; set; } = new();
}

public class LoginPolicySettings
{
    public int IpMaxCount { get; set; } = 20;
    public int IpDurationMinutes { get; set; } = 15;
    public int EmailMaxCount { get; set; } = 5;
    public int EmailDurationMinutes { get; set; } = 15;
}

public class SmsVerificationCodePolicySettings
{
    public int MaxCount { get; set; } = 3;
    public int DurationHours { get; set; } = 1;
}

public class SmsNotificationPolicySettings
{
    public int MaxCount { get; set; } = 20;
    public int DurationHours { get; set; } = 1;
}

public class EmailVerificationCodePolicySettings
{
    public int MaxCount { get; set; } = 5;
    public int DurationHours { get; set; } = 1;

    /// <summary>
    /// round3 补的 IP 并联规则阈值（同窗口）：防攻击者换 IP 定向烧光受害者发码配额；
    /// 默认 30 次/小时，放宽是给共享出口 NAT 的正常用户留余量。
    /// </summary>
    public int IpMaxCount { get; set; } = 30;
}

public class VerificationCodeVerifyPolicySettings
{
    public int MaxCount { get; set; } = 5;
    public int DurationMinutes { get; set; } = 10;

    /// <summary>
    /// round3 补的 IP 并联规则阈值（同窗口，邮件/短信校验侧共用）：防换 IP 定向烧光受害者验码配额。
    /// </summary>
    public int IpMaxCount { get; set; } = 30;
}

public class TwoFactorCodePolicySettings
{
    public int MaxCount { get; set; } = 5;
    public int DurationMinutes { get; set; } = 10;
}

public class AuditLogExportPolicySettings
{
    public int MaxCount { get; set; } = 10;
    public int DurationDays { get; set; } = 1;
}

public class FileShareDownloadPolicySettings
{
    public int IpMaxCount { get; set; } = 30;
    public int IpDurationMinutes { get; set; } = 1;
}

public class GdprDownloadPolicySettings
{
    public int IpMaxCount { get; set; } = 10;
    public int IpDurationMinutes { get; set; } = 1;
}

public class CaptchaImagePolicySettings
{
    public int IpMaxCount { get; set; } = 60;
    public int IpDurationMinutes { get; set; } = 5;
}
