using Microsoft.AspNetCore.Http;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Profile;

/// <summary>
/// <see cref="IAvatarCachePolicyApplier"/> 的宿主侧实现：把缓存策略写入当前 HTTP
/// 响应。头像 URL 带 ?v={version}、内容按版本变化，可安全长缓存（策略常量与
/// 语义见 <see cref="AvatarCacheConsts"/>）。
/// </summary>
public class AvatarCachePolicyApplier : IAvatarCachePolicyApplier, ITransientDependency
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AvatarCachePolicyApplier(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Apply(string? etagVersion)
    {
        var response = _httpContextAccessor.HttpContext?.Response;
        if (response is null)
        {
            return;
        }

        response.Headers.CacheControl = AvatarCacheConsts.CacheControlHeaderValue;
        if (!string.IsNullOrWhiteSpace(etagVersion))
        {
            response.Headers.ETag = $"\"{etagVersion}\"";
        }
    }
}
