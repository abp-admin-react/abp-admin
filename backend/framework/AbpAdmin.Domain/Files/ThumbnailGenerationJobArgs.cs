using System;

namespace AbpAdmin.Files;

/// <summary>
/// 缩略图生成后台作业参数（T3.1）。TenantId 必须随作业走——
/// 后台作业不继承上传请求的租户上下文（00-overview 6.5）。
/// </summary>
[Serializable]
public class ThumbnailGenerationJobArgs
{
    public Guid? TenantId { get; set; }

    public Guid FileId { get; set; }

    public string FileName { get; set; } = default!;
}
