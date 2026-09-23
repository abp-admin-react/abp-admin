namespace AbpAdmin.Sms;

/// <summary>
/// 日志/异常里的手机号打码（T3.5）：138****8000。
/// 注意只用于日志与异常渲染；限流分区键必须用完整号码（打码后不同号码会撞分区）。
/// </summary>
public static class AbpAdminSmsLogMasker
{
    public static string MaskPhoneNumber(string? phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
        {
            return "***";
        }

        return phoneNumber.Length <= 7
            ? phoneNumber[..1] + "****"
            : phoneNumber[..3] + "****" + phoneNumber[^4..];
    }
}
