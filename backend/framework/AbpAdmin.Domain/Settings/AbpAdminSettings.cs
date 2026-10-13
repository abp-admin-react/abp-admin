namespace AbpAdmin.Settings;

/// <summary>
/// AbpAdmin 自定义设置项名称。按业务域分组为嵌套静态类（System/Account/Audit/Saas/Sms/Payment），
/// 与按域拆分的各 SettingDefinitionProvider 一一对应。
/// 使用嵌套类形式访问（如 AbpAdminSettings.Sms.Provider）。
/// </summary>
public static class AbpAdminSettings
{
    private const string Prefix = "AbpAdmin";

    /// <summary>系统通用域（Group1 = System）。</summary>
    public static class System
    {
        /// <summary>演示自定义设置：站点标题。用于验证「新增 SettingDefinition 不改前端即出现」。</summary>
        public const string SiteTitle = Prefix + ".SiteTitle";

        /// <summary>演示自定义设置：是否开启维护模式（checkbox）。</summary>
        public const string MaintenanceMode = Prefix + ".MaintenanceMode";

        /// <summary>演示自定义设置：每页最大条数（number）。</summary>
        public const string MaxPageSize = Prefix + ".MaxPageSize";

        /// <summary>演示自定义设置：默认主题（select）。</summary>
        public const string DefaultTheme = Prefix + ".DefaultTheme";

        /// <summary>演示自定义设置：欢迎消息（text）。用于 T1.4.8 验收「新增 SettingDefinition 不改前端即出现」。</summary>
        public const string WelcomeMessage = Prefix + ".WelcomeMessage";
    }

    /// <summary>审计域（Group1 = Audit）。</summary>
    public static class Audit
    {
        /// <summary>审计日志过期清理总开关（host 侧）。关闭时租户设置无效。</summary>
        public const string ExpiredItemDeletionEnabled = Prefix + ".Audit.ExpiredItemDeletionEnabled";

        /// <summary>审计日志保留天数。默认 30 天，允许租户覆盖。</summary>
        public const string ExpiredItemDeletionPeriodDays = Prefix + ".Audit.ExpiredItemDeletionPeriodDays";
    }

    /// <summary>账户/登录安全域（Group1 = Account）。</summary>
    public static class Account
    {
        /// <summary>无密码登录模式：OtpAndMagicLink（默认）| MagicLinkOnly | OtpOnly</summary>
        public const string PasswordlessLoginMode = Prefix + ".Account.PasswordlessLoginMode";

        /// <summary>无密码登录 token 有效期（秒）。默认 90 秒，范围 30-86400。</summary>
        public const string PasswordlessLoginTokenLifetimeSeconds = Prefix + ".Account.PasswordlessLoginTokenLifetimeSeconds";

        /// <summary>是否启用防邮箱枚举。默认 true。</summary>
        public const string PreventEmailEnumeration = Prefix + ".Account.PreventEmailEnumeration";

        /// <summary>不活跃会话保留天数。CleanupWorker 按它删除。0 表示不清理。</summary>
        public const string SessionCleanupInactiveDays = Prefix + ".Identity.SessionCleanupInactiveDays";

        /// <summary>
        /// 防并发登录模式（对标 ABP Identity Pro）：Disabled（默认）| LogoutFromSameTypeDevices | LogoutFromAllDevices。
        /// 存枚举名字符串，登录建会话时由 IdentitySessionManager 执行。
        /// </summary>
        public const string PreventConcurrentLoginMode = Prefix + ".Identity.PreventConcurrentLoginMode";

        /// <summary>SPA 空闲超时分钟数。0 表示关闭。下发给客户端。</summary>
        public const string IdleSessionTimeoutMinutes = Prefix + ".Account.IdleSessionTimeoutMinutes";

        /// <summary>是否启用 Cloudflare Turnstile。</summary>
        public const string CaptchaEnabled = Prefix + ".Account.Captcha.Enabled";

        /// <summary>
        /// 验证码实现：Turnstile（默认，外置行为验证）/ Image（自托管图形验证码，无外部依赖）。
        /// 仅在 Captcha.Enabled 为 true 时生效。
        /// </summary>
        public const string CaptchaProvider = Prefix + ".Account.Captcha.Provider";

        /// <summary>Turnstile Site Key（可下发前端）。</summary>
        public const string CaptchaSiteKey = Prefix + ".Account.Captcha.SiteKey";

        /// <summary>Turnstile Secret Key（加密）。</summary>
        public const string CaptchaSecretKey = Prefix + ".Account.Captcha.SecretKey";

        /// <summary>外部登录：GitHub 启用开关</summary>
        public const string ExternalLoginGitHubEnabled = Prefix + ".Account.ExternalLogin.GitHub.Enabled";

        /// <summary>外部登录：GitHub ClientId</summary>
        public const string ExternalLoginGitHubClientId = Prefix + ".Account.ExternalLogin.GitHub.ClientId";

        /// <summary>外部登录：GitHub ClientSecret（加密存储）</summary>
        public const string ExternalLoginGitHubClientSecret = Prefix + ".Account.ExternalLogin.GitHub.ClientSecret";

        /// <summary>外部登录：Microsoft 启用开关</summary>
        public const string ExternalLoginMicrosoftEnabled = Prefix + ".Account.ExternalLogin.Microsoft.Enabled";

        /// <summary>外部登录：Microsoft ClientId</summary>
        public const string ExternalLoginMicrosoftClientId = Prefix + ".Account.ExternalLogin.Microsoft.ClientId";

        /// <summary>外部登录：Microsoft ClientSecret（加密存储）</summary>
        public const string ExternalLoginMicrosoftClientSecret = Prefix + ".Account.ExternalLogin.Microsoft.ClientSecret";

        /// <summary>外部登录：微信开放平台启用开关</summary>
        public const string ExternalLoginWeixinEnabled = Prefix + ".Account.ExternalLogin.Weixin.Enabled";

        public const string ExternalLoginWeixinClientId = Prefix + ".Account.ExternalLogin.Weixin.ClientId";

        public const string ExternalLoginWeixinClientSecret = Prefix + ".Account.ExternalLogin.Weixin.ClientSecret";

        public const string ExternalLoginGoogleEnabled = Prefix + ".Account.ExternalLogin.Google.Enabled";

        public const string ExternalLoginGoogleClientId = Prefix + ".Account.ExternalLogin.Google.ClientId";

        public const string ExternalLoginGoogleClientSecret = Prefix + ".Account.ExternalLogin.Google.ClientSecret";
    }

    /// <summary>SaaS 域（Group1 = Saas，无二级分块）。</summary>
    public static class Saas
    {
        /// <summary>
        /// 是否启用按租户管理连接字符串（默认 true）。
        /// 禁用时：前端隐藏连接串管理 UI、创建租户时忽略传入的连接串、更新连接串的请求返回业务异常。
        /// </summary>
        public const string EnableTenantBasedConnectionStringManagement = Prefix + ".Saas.EnableTenantBasedConnectionStringManagement";
    }

    /// <summary>短信渠道域（Group1 = Sms）。</summary>
    public static class Sms
    {
        /// <summary>短信厂商：Null（默认）/ Aliyun / TencentCloud。SmsSenderResolver 按它分发。</summary>
        public const string Provider = Prefix + ".Sms.Provider";

        /// <summary>阿里云短信 AccessKeyId。</summary>
        public const string AliyunAccessKeyId = Prefix + ".Sms.Aliyun.AccessKeyId";

        /// <summary>阿里云短信 AccessKeySecret（加密存储）。</summary>
        public const string AliyunAccessKeySecret = Prefix + ".Sms.Aliyun.AccessKeySecret";

        /// <summary>阿里云短信签名（SmsMessage.Properties 未带 SignName 时的缺省值）。</summary>
        public const string AliyunSignName = Prefix + ".Sms.Aliyun.SignName";

        /// <summary>腾讯云短信 SecretId。</summary>
        public const string TencentCloudSecretId = Prefix + ".Sms.TencentCloud.SecretId";

        /// <summary>腾讯云短信 SecretKey（加密存储）。</summary>
        public const string TencentCloudSecretKey = Prefix + ".Sms.TencentCloud.SecretKey";

        /// <summary>腾讯云短信 EndPoint，默认 sms.tencentcloudapi.com。</summary>
        public const string TencentCloudEndPoint = Prefix + ".Sms.TencentCloud.EndPoint";

        /// <summary>腾讯云短信 Region，默认 ap-guangzhou。</summary>
        public const string TencentCloudRegion = Prefix + ".Sms.TencentCloud.Region";

        /// <summary>腾讯云短信应用 SmsSdkAppid。</summary>
        public const string TencentCloudSmsSdkAppid = Prefix + ".Sms.TencentCloud.SmsSdkAppid";

        /// <summary>腾讯云短信签名。</summary>
        public const string TencentCloudSign = Prefix + ".Sms.TencentCloud.Sign";

        /// <summary>
        /// 手机验证码（T2.7）使用的模板编码：阿里云填模板 CODE，腾讯云填 TemplateID（同一个设置项两家复用）。
        /// 模板参数名约定为 code（腾讯模板按位置取第一个参数）。
        /// </summary>
        public const string VerificationCodeTemplateCode = Prefix + ".Sms.VerificationCode.TemplateCode";
    }

    /// <summary>支付域（Group1 = Payment）。</summary>
    public static class Payment
    {
        public const string WeChatPayMchId = Prefix + ".Payment.WeChatPay.MchId";

        public const string WeChatPayApiKey = Prefix + ".Payment.WeChatPay.ApiKey";

        public const string WeChatPayCertificate = Prefix + ".Payment.WeChatPay.Certificate";

        public const string WeChatPayCertificateSecret = Prefix + ".Payment.WeChatPay.CertificateSecret";

        public const string WeChatPayNotifyUrl = Prefix + ".Payment.WeChatPay.NotifyUrl";
    }
}
