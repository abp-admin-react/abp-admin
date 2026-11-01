namespace AbpAdmin.Files;

/// <summary>
/// 文件管理容器约束配置，绑定 appsettings.json 的 FileManagement 节。
/// 具体数值不要硬编码在模块代码里，统一走配置。
/// </summary>
public class AbpAdminFileManagementOptions
{
    public const string SectionName = "FileManagement";

    /// <summary>单文件最大字节数，默认 100 MB。</summary>
    public long MaxByteSizeForEachFile { get; set; } = 100L * 1024 * 1024;

    /// <summary>单次上传总字节数上限，默认 200 MB。</summary>
    public long MaxByteSizeForEachUpload { get; set; } = 200L * 1024 * 1024;

    /// <summary>单次上传文件数量上限。</summary>
    public int MaxFileQuantityForEachUpload { get; set; } = 20;

    /// <summary>是否只允许白名单扩展名。</summary>
    public bool AllowOnlyConfiguredFileExtensions { get; set; } = true;

    /// <summary>每用户每分钟获取下载凭证次数上限。</summary>
    public int GetDownloadInfoTimesLimitEachUserPerMinute { get; set; } = 60;

    /// <summary>扩展名白名单。</summary>
    public string[] AllowedFileExtensions { get; set; } =
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp", ".pdf",
        ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
        ".txt", ".md", ".csv", ".zip"
    };
}
