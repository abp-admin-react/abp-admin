using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AbpAdmin.RateLimiting;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.DistributedLocking;
using Volo.Abp.Domain.Services;
using Volo.Abp.Identity;
using Volo.Abp.Users;

namespace AbpAdmin.Account;

/// <summary>
/// T2.7 无密码登录凭据的校验与消费（Domain 层，Application.Tests 可直接覆盖）。
/// 一次性语义：验证码与 Magic Link token 共享同一条缓存记录，任一路径成功后整条删除，
/// 另一路径立即失效；重发由 AccountProAppService 先删旧凭据再写新凭据。
/// 令牌签发由 HttpApi.Host 的 PasswordlessTokenExtensionGrant 调用本类完成。
/// </summary>
public class PasswordlessLoginManager : DomainService
{
    private readonly IdentityUserManager _userManager;
    private readonly IDistributedCache<PasswordlessLoginTokenCacheItem> _passwordlessTokenCache;
    private readonly IOperationRateLimitingChecker _rateLimitingChecker;
    private readonly IAbpDistributedLock _distributedLock;

    public PasswordlessLoginManager(
        IdentityUserManager userManager,
        IDistributedCache<PasswordlessLoginTokenCacheItem> passwordlessTokenCache,
        IOperationRateLimitingChecker rateLimitingChecker,
        IAbpDistributedLock distributedLock)
    {
        _userManager = userManager;
        _passwordlessTokenCache = passwordlessTokenCache;
        _rateLimitingChecker = rateLimitingChecker;
        _distributedLock = distributedLock;
    }

    /// <summary>
    /// 缓存键：租户 + 大写邮箱。发码（AppService）与验码（grant）必须使用同一个键。
    /// </summary>
    public virtual string BuildCacheKey(Guid? tenantId, string email)
    {
        return $"{tenantId?.ToString() ?? "host"}:{email.ToUpperInvariant()}";
    }

    /// <summary>
    /// 校验并消费一次性凭据。code 或 magicLinkToken 匹配其一即视为有效；
    /// 成功后整条缓存删除（另一路径立即失效）并重置校验侧限流；失败返回 null。
    /// 校验侧限流（防暴力猜码）在比对前计数。
    /// </summary>
    public virtual async Task<IdentityUser?> ValidateAndConsumeAsync(string email, string? code, string? magicLinkToken)
    {
        await _rateLimitingChecker.CheckAsync(OperationRateLimitingPolicyNames.EmailVerificationCodeVerify, email);

        var cacheKey = BuildCacheKey(CurrentTenant.Id, email);

        // 单次消费原子化（与 GDPR 下载 token 的 ConsumeDownloadTokenAsync 同型）：
        // ABP 的 IDistributedCache.RemoveAsync 不返回被删条目（无 Redis GETDEL 语义），
        // 原先「读 → 验 → 删」三步非原子，并发重放可双双通过校验各自换取令牌。
        // 分布式锁把三步收进临界区：并发重放只有一个能在锁内命中缓存条目，
        // 删除释放后后来者读到的必是未命中。
        await using var handle = await _distributedLock.TryAcquireAsync(
            $"passwordless-login:{cacheKey}",
            TimeSpan.FromSeconds(10));

        if (handle == null)
        {
            // 锁竞争超时是基础设施故障：抛 500（AbpException），不与「凭据无效返回 null」混淆
            throw new AbpException($"获取无密码登录凭据消费锁超时：{cacheKey}");
        }

        var cacheItem = await _passwordlessTokenCache.GetAsync(cacheKey);

        // 比对改 CryptographicOperations.FixedTimeEquals（UTF8 字节级常数时间）：
        // string == 按字符比较、首字符不同即返回，理论上可作时间侧信道逐位猜码
        //（校验侧限流 5 次/10 分钟已缓解，这里把残余风险也消掉）。
        var isValid = cacheItem != null
                      && cacheItem.ExpiresAt >= Clock.Now
                      && ((!code.IsNullOrWhiteSpace() && FixedTimeEquals(cacheItem.Code, code))
                          || (!magicLinkToken.IsNullOrWhiteSpace() && FixedTimeEquals(cacheItem.MagicLinkToken, magicLinkToken)));

        if (!isValid)
        {
            return null;
        }

        await _passwordlessTokenCache.RemoveAsync(cacheKey);
        await _rateLimitingChecker.ResetAsync(OperationRateLimitingPolicyNames.EmailVerificationCodeVerify, email);

        // 账户状态在换票时刻复核（fail closed）：发码侧只查了锁定没查 IsActive——管理员停用后，
        // 已送达邮箱的 OTP/magic link 在有效期内仍可换票；同理，密码侧爆破触发的锁定不应被
        // 仍有效的 OTP 绕过。凭据已在上面消费（一次性语义保持），状态不符统一按「凭据无效」
        // 返回 null，不区分具体原因（防枚举口径与验码失败一致）。
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null || !user.IsActive || await _userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        return user;
    }

    /// <summary>
    /// 常数时间比对（UTF8 字节）。长度不等时 FixedTimeEquals 立即返回 false——
    /// 长度不是敏感信息（code 固定 6 位数字、magic link token 固定 32 字符十六进制），可接受。
    /// </summary>
    private static bool FixedTimeEquals(string expected, string actual)
    {
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }
}
