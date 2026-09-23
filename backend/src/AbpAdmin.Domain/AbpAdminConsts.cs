using Volo.Abp.Identity;

namespace AbpAdmin;

public static class AbpAdminConsts
{
    public const string DbTablePrefix = "App";
    public const string? DbSchema = null;
    public const string AdminEmailDefaultValue = IdentityDataSeedContributor.AdminEmailDefaultValue;
    public const string AdminPasswordDefaultValue = "1q2w3E*";

    /// <summary>
    /// T3.1：头像版本号在 IdentityUser.ExtraProperties 里的 key。
    /// 前端拼头像 URL 时带 ?v={version} 击穿插件/CDN/浏览器缓存；不建真实列（从不按它查询）。
    /// </summary>
    public const string AvatarVersionPropertyName = "AvatarVersion";

    /// <summary>
    /// 审查轮：头像 blob 实际落盘扩展名在 IdentityUser.ExtraProperties 里的 key
    /// （blob 名按上传时的真实格式生成，SkiaSharp contributor 不转码）。GDPR 导出/删户清理
    /// 以它为准精确删除，当前白名单候选只作历史数据兜底——白名单日后收窄不残留个人数据。
    /// </summary>
    public const string AvatarBlobExtensionPropertyName = "AvatarBlobExtension";
}
