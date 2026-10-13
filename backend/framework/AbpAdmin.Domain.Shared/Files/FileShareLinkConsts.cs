namespace AbpAdmin.Files;

/// <summary>
/// 文件分享链接约束。过期上限防止客户端把链接设到遥远未来；token 长度与库列一致。
/// </summary>
public static class FileShareLinkConsts
{
    public const int MaxTokenLength = 64;

    /// <summary>相对现在最多延长这么多天。超过则创建失败，不截断。</summary>
    public const int MaxExpireDays = 30;
}
