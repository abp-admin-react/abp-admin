namespace AbpAdmin.Profile;

/// <summary>
/// 头像响应缓存策略（T3.1 / 重构报告问题 18）：
/// 头像内容按版本变化、URL 带 ?v={version} 参数，可安全长缓存。
/// max-age 集中为常量，避免散落的魔法字符串（86400 秒 = 24 小时）。
/// </summary>
public static class AvatarCacheConsts
{
    /// <summary>Cache-Control 响应头值（私有缓存，24 小时）。</summary>
    public const string CacheControlHeaderValue = "private, max-age=86400";
}
