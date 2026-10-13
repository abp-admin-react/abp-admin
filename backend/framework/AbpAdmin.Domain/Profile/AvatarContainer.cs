using Volo.Abp.BlobStoring;

namespace AbpAdmin.Profile;

/// <summary>
/// 用户头像 BLOB 容器（T3.1）。provider 跟随 T0.2 的默认容器配置，
/// 多租户隔离由 ABP 的 IBlobContainer（IsMultiTenant = true）天然提供。
/// </summary>
[BlobContainerName("user-avatars")]
public class AvatarContainer
{
}
