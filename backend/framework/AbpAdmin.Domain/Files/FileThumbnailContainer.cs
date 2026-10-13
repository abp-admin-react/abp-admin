using Volo.Abp.BlobStoring;

namespace AbpAdmin.Files;

/// <summary>
/// 缩略图独立 BLOB 容器（T3.1）。独立容器可以在 BlobStoring 配置里单独指定 provider
/// 与生命周期策略（缩略图可随时重建），同容器子目录做不到。
/// </summary>
[BlobContainerName("file-thumbnails")]
public class FileThumbnailContainer
{
}
