namespace AbpAdmin.Sms;

/// <summary>
/// 短信厂商标识（T3.5），即 <see cref="Settings.AbpAdminSettings.Sms.Provider"/> 设置项的可选值。
/// </summary>
public static class AbpAdminSmsProviders
{
    /// <summary>未配置：发送不抛异常，日志记录"厂商未配置"。</summary>
    public const string Null = "Null";

    public const string Aliyun = "Aliyun";

    public const string TencentCloud = "TencentCloud";
}
