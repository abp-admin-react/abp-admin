using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.Profile;
using Volo.Abp;
using Volo.Abp.BlobStoring;

namespace AbpAdmin.Imaging;

/// <summary>
/// 头像 blob「探测读取」的单一所有者（审查轮收拢 ProfileAvatarAppService / GDPR Provider 的双份循环）：
/// 用户 ExtraProperties 里持久化的实际扩展名（AbpAdminConsts.AvatarBlobExtensionPropertyName，上传时刻
/// 落盘的真实格式）优先精确命中，其余当前白名单候选仅作历史数据兜底。
/// </summary>
public static class AvatarBlobReader
{
    /// <summary>找到第一个存在的头像 blob 并读出字节；不存在返回 null。</summary>
    public static async Task<(string Extension, byte[] Bytes)?> FindFirstAsync(
        IBlobContainer<AvatarContainer> container,
        AbpAdminImagingOptions options,
        Guid userId,
        string? storedExtension)
    {
        // 有序候选：持久化的实际扩展名优先，当前白名单兜底（白名单收窄场景）
        var candidates = new List<string>();
        if (!storedExtension.IsNullOrWhiteSpace())
        {
            candidates.Add(storedExtension);
        }

        foreach (var candidate in AvatarBlobNames.CandidateExtensions(options))
        {
            if (candidate != storedExtension)
            {
                candidates.Add(candidate);
            }
        }

        foreach (var extension in candidates)
        {
            var blobName = AvatarBlobNames.ForUser(userId, extension);
            if (!await container.ExistsAsync(blobName))
            {
                continue;
            }

            return (extension, await container.GetAllBytesAsync(blobName));
        }

        return null;
    }
}
