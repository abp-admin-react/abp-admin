using AbpAdmin.TokenGateway.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.Identity;

namespace AbpAdmin.TokenGateway.Http;

public record LoginInput(string? Username, string? UserNameOrEmail, string? Password);

[Route("api/auth")]
public class AuthController(IdentityUserManager users, KeyStore keys, Wallet wallet, Microsoft.Extensions.Options.IOptions<TokenGatewayOptions> options, ILogger<AuthController> log) : GatewayControllerBase
{
    /// <summary>abp_admin 账号密码登录 → 会话令牌 + 用户 Key（同一用户复用同一把 hb-）。</summary>
    [HttpPost("login")]
    public Task<IActionResult> Login([FromBody] LoginInput input) => Run(async () =>
    {
        var name = input.Username ?? input.UserNameOrEmail;
        var bad = new GatewayException(GatewayErrorCodes.InvalidCredentials, 401, "账号或密码错误");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrEmpty(input.Password)) throw bad;

        var user = await users.FindByNameAsync(name) ?? await users.FindByEmailAsync(name);
        if (user is null || !user.IsActive || await users.IsLockedOutAsync(user)) throw bad;
        if (!await users.CheckPasswordAsync(user, input.Password))
        {
            await users.AccessFailedAsync(user);
            throw bad;
        }
        await users.ResetAccessFailedCountAsync(user);

        var trial = options.Value.TrialTokens;
        if (trial > 0) wallet.Credit(user.Id, trial, "trial");
        var (session, exp) = keys.IssueSession(user.Id);
        return Ok(new
        {
            session,
            sessionExpiresAt = exp,
            apiKey = keys.IssueOrReuseKey(user.Id),
            user = new { id = user.Id, userName = user.UserName, name = user.Name },
        });
    }, log);
}
