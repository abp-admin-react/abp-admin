namespace AbpAdmin.Identity;

/// <summary>
/// 防并发登录模式（对标 ABP Identity Pro 的 Prevent Concurrent Login 三档）。
/// 存储取枚举名字符串（"Disabled"/"LogoutFromSameTypeDevices"/"LogoutFromAllDevices"），
/// 解析端 Enum.TryParse 忽略大小写。
/// </summary>
public enum PreventConcurrentLoginMode
{
    /// <summary>不限制并发登录（默认）。</summary>
    Disabled = 0,

    /// <summary>同类型设备仅保留最新会话（浏览器互踢，移动端各自独立）。</summary>
    LogoutFromSameTypeDevices = 1,

    /// <summary>新会话建立时登出该用户全部其它会话。</summary>
    LogoutFromAllDevices = 2
}
