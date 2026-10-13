using System;

namespace AbpAdmin.Account;

/// <summary>
/// 邮箱确认 6 位数字码的缓存项（审查 E2E 轮引入：原先用 Identity 的
/// GenerateEmailConfirmationTokenAsync 生成 200+ 字符 SecureToken 作「确认码」，
/// 与前端 6 位输入框语义不匹配，确认闭环走不通；改为 6 位数字码 + 缓存 10 分钟，
/// 与手机号确认（GenerateChangePhoneNumberTokenAsync，6 位数字）口径对齐。
/// 只存 SHA-256 哈希不存明文；校验侧比对用常数时间比较（与免密登录同法）。
/// </summary>
public class EmailConfirmationCodeCacheItem
{
    public string? CodeHash { get; set; }

    public DateTime ExpiresAt { get; set; }

    public bool IsValid(Guid? tenantId, string email, string code, DateTime now)
    {
        return ExpiresAt >= now
               && !string.IsNullOrWhiteSpace(CodeHash)
               && FixedTimeEquals(CodeHash, Hash(code, tenantId, email));
    }

    public static string Hash(string code, Guid? tenantId, string email)
    {
        var payload = $"{tenantId?.ToString() ?? "host"}:{email.ToUpperInvariant()}:{code}";
        return Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(payload)));
    }

    /// <summary>常数时间比对（UTF8 字节级），防时序侧信道逐位猜码。</summary>
    private static bool FixedTimeEquals(string expected, string actual)
    {
        return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(expected),
            System.Text.Encoding.UTF8.GetBytes(actual));
    }
}
