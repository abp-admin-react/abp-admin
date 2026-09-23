using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Volo.Abp.Caching;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Captcha;

/// <summary>
/// 自托管图形验证码的服务端状态（对标 RuoYi sys 登录验证码）。
/// 只存答案的 SHA-256（不存明文），一次性：校验即消费。
/// </summary>
[Serializable]
[IgnoreMultiTenancy]
public class CaptchaCodeCacheItem
{
    public string CodeHash { get; set; } = default!;

    public static string ComputeHash(string code)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(code.Trim().ToUpperInvariant()));
        return Convert.ToHexString(bytes);
    }
}

/// <summary>图形验证码校验器：生成侧由 CaptchaImageAppService 调 RememberAsync 存答案哈希。</summary>
public class ImageCaptchaManager : Volo.Abp.Domain.Services.DomainService
{
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(5);

    private readonly IDistributedCache<CaptchaCodeCacheItem> _cache;

    public ImageCaptchaManager(IDistributedCache<CaptchaCodeCacheItem> cache)
    {
        _cache = cache;
    }

    public virtual async Task RememberAsync(Guid id, string code, TimeSpan? lifetime = null)
    {
        await _cache.SetAsync(
            CalculateCacheKey(id),
            new CaptchaCodeCacheItem { CodeHash = CaptchaCodeCacheItem.ComputeHash(code) },
            new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = lifetime ?? DefaultLifetime
            });
    }

    /// <summary>
    /// 校验并消费（一次性：无论对错都删，防止对同一张图穷举）。
    /// 校验不区分大小写、忽略首尾空白。
    /// </summary>
    public virtual async Task<bool> ValidateAndConsumeAsync(Guid id, string? code)
    {
        var key = CalculateCacheKey(id);
        var item = await _cache.GetAsync(key);
        if (item == null)
        {
            return false;
        }

        // 先删后比：删除失败（理论上的竞态）只会导致消费不彻底，不会导致绕过
        await _cache.RemoveAsync(key);

        return string.Equals(
            item.CodeHash,
            CaptchaCodeCacheItem.ComputeHash(code ?? string.Empty),
            StringComparison.Ordinal);
    }

    public static string CalculateCacheKey(Guid id)
    {
        return "captcha:image:" + id.ToString("N");
    }
}
