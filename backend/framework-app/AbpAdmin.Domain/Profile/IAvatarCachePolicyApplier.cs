namespace AbpAdmin.Profile;

/// <summary>
/// 头像响应缓存头的写入端口：Application 层只决定策略（版本号、常量见
/// <see cref="AvatarCacheConsts"/>），响应头属 Http 关注点，由宿主侧实现写入——
/// Application 不直接触碰 HttpContext。非 Web 环境（集成测试直调 AppService）
/// 没有实现，解析为 null 即自然跳过。
/// </summary>
public interface IAvatarCachePolicyApplier
{
    /// <summary>
    /// 对当前 HTTP 响应应用头像缓存策略（Cache-Control 长缓存 + 版本 ETag）。
    /// </summary>
    /// <param name="etagVersion">头像版本号（AvatarVersion 扩展属性）；为空则只设 Cache-Control。</param>
    void Apply(string? etagVersion);
}
