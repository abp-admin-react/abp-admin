using System;

namespace AbpAdmin.Pages.Account;

/// <summary>
/// 登录成功回跳前丢掉会把允许的源嵌进另一个主机的地址。
/// 允许列表若按前缀比较，<c>http://localhost:8100@evil.com</c> 会被当成站内地址。
/// </summary>
internal static class ReturnUrlAuthorityGuard
{
    public static void Sanitize(ref string? returnUrl, ref string? returnUrlHash)
    {
        if (!string.IsNullOrEmpty(returnUrlHash) &&
            (returnUrlHash.Contains('@', StringComparison.Ordinal) ||
             returnUrlHash.Contains("://", StringComparison.Ordinal)))
        {
            returnUrlHash = null;
        }

        if (string.IsNullOrWhiteSpace(returnUrl) ||
            !Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri) ||
            string.IsNullOrEmpty(uri.UserInfo))
        {
            return;
        }

        returnUrl = "~/";
        returnUrlHash = null;
    }
}
