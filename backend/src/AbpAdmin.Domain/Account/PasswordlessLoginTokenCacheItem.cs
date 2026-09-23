using System;

namespace AbpAdmin.Account;

/// <summary>
/// 无密码登录 token 缓存项。验证码与 Magic Link token 共享同一条记录，任一路径成功后整条删除。
/// </summary>
[Serializable]
public class PasswordlessLoginTokenCacheItem
{
    public string Email { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string MagicLinkToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
}
