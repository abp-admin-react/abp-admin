using System;
using System.Threading.Tasks;
using AbpAdmin.RateLimiting;
using AbpAdmin.TokenGateway.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.Identity;

namespace AbpAdmin.TokenGateway.Http;

public record LoginInput(string? Username, string? UserNameOrEmail, string? Password);

// Route 必须显式声明（无 attribute 路由的动作不生成可路由端点，
// 请求撞上宿主 FallbackPolicy 的匿名拒绝变成裸 401）
[Route("api/auth")]
public class AuthController(IdentityUserManager users, KeyStore keys, Wallet wallet,
    IOperationRateLimitingChecker rateLimits, IOptions<TokenGatewayOptions> options, ILogger<AuthController> log) : GatewayControllerBase
{
    /// <summary>
    /// 时序抹平用的假哈希（固定随机盐）：用户名不存在时也执行一次等价成本的 BCrypt 校验，
    /// 让「用户不存在」与「密码错误」的响应时间曲线一致，无法靠耗时差批量枚举账号。
    /// </summary>
    private static readonly string DummyHash =
        new Microsoft.AspNetCore.Identity.PasswordHasher<Volo.Abp.Identity.IdentityUser>()
            .HashPassword(new Volo.Abp.Identity.IdentityUser(Guid.NewGuid(), "timing-equalizer", "timing-equalizer"), Guid.NewGuid().ToString("N"));

    /// <summary>
    /// abp_admin 账号密码登录 → 会话令牌 + 用户 Key（同一用户复用同一把 hb-）。
    /// 本端点是匿名凭据校验面，不走宿主登录页/Turnstile/令牌端点限流——仅按账号的 Identity
    /// 锁定计数防不住跨账号 password spraying。复用宿主
    /// OperationRateLimiting 基建：先 TokenEndpoint（按 IP 宽闸兜底），再 Login
    /// （IP+用户名双规则，与页面登录同一策略，阈值可在 OperationRateLimiting 配置调）。
    /// </summary>
    [HttpPost("login")]
    public Task<IActionResult> Login([FromBody] LoginInput input) => Run(async () =>
    {
        var name = input.Username ?? input.UserNameOrEmail;
        var bad = new GatewayException(GatewayErrorCodes.InvalidCredentials, 401, "账号或密码错误");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(input.Password)) throw bad;

        try
        {
            await rateLimits.CheckAsync(OperationRateLimitingPolicyNames.TokenEndpoint);
            await rateLimits.CheckAsync(OperationRateLimitingPolicyNames.Login, name);
        }
        catch (AbpAdminOperationRateLimitingException ex)
        {
            // 与宿主令牌端点同语义：429 + Retry-After（必须下发——
            // 爆破客户端拿到退避时间才不会按固定节奏撞限）
            if (ex.Data.Contains("RetryAfterSeconds") &&
                double.TryParse(ex.Data["RetryAfterSeconds"]?.ToString(), out var retryAfterSeconds))
            {
                Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfterSeconds)).ToString();
            }
            throw GatewayException.RateLimited();
        }

        var user = await users.FindByNameAsync(name) ?? await users.FindByEmailAsync(name);
        if (user is null || !user.IsActive || await users.IsLockedOutAsync(user))
        {
            users.PasswordHasher.VerifyHashedPassword(new Volo.Abp.Identity.IdentityUser(Guid.NewGuid(), "timing-equalizer", "timing-equalizer"), DummyHash, input.Password);
            throw bad;
        }
        if (!await users.CheckPasswordAsync(user, input.Password))
        {
            await users.AccessFailedAsync(user);
            throw bad;
        }
        await users.ResetAccessFailedCountAsync(user);

        var trial = options.Value.TrialTokens;
        if (trial > 0) await wallet.CreditAsync(user.Id, trial, "trial:" + user.Id.ToString("N"));
        var (session, exp) = await keys.IssueSessionAsync(user.Id);
        return Ok(new
        {
            session,
            sessionExpiresAt = exp,
            apiKey = await keys.IssueOrReuseKeyAsync(user.Id),
            user = new { id = user.Id, userName = user.UserName, name = user.Name },
        });
    }, log);
}
