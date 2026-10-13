using System;

namespace AbpAdmin.Gdpr;

/// <summary>
/// GDPR 下载 token 缓存项。token 即凭据，60 分钟过期。
/// </summary>
[Serializable]
public class GdprDownloadTokenCacheItem
{
    public Guid RequestId { get; set; }

    public Guid UserId { get; set; }

    public GdprDownloadTokenCacheItem()
    {
    }

    public GdprDownloadTokenCacheItem(Guid requestId, Guid userId)
    {
        RequestId = requestId;
        UserId = userId;
    }
}
