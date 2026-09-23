using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Timing;
using AbpAdmin.OpenIddict;
using AbpAdmin.RateLimiting;
using Xunit;

namespace AbpAdmin.Account;

/* T2.7 无密码登录集成测试。
 * 覆盖：一次性语义（码成功则链接失效、反之亦然）、重发使旧凭据失效、
 * 重发重置校验侧限流、token 有效期钳制（设 10 → 生效 30）、邮件内容随模式变化、
 * 并发重放同一验证码恰好消费一次（分布式锁内「读-验-删」原子化）。
 * 校验/消费逻辑在 Domain 层 PasswordlessLoginManager（grant 也走它），可直接测试。
 */
public abstract class PasswordlessLoginTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IAccountProAppService _accountProAppService;
    private readonly PasswordlessLoginManager _passwordlessLoginManager;
    private readonly IDistributedCache<PasswordlessLoginTokenCacheItem> _passwordlessTokenCache;
    private readonly IdentityUserManager _userManager;
    private readonly ISettingManager _settingManager;
    private readonly RecordingEmailSender _emailSender;
    private readonly IClock _clock;

    private AbpAdminOperationRateLimitingOptions RateLimitingOptions =>
        GetRequiredService<IOptions<AbpAdminOperationRateLimitingOptions>>().Value;

    protected PasswordlessLoginTests()
    {
        _accountProAppService = GetRequiredService<IAccountProAppService>();
        _passwordlessLoginManager = GetRequiredService<PasswordlessLoginManager>();
        _passwordlessTokenCache = GetRequiredService<IDistributedCache<PasswordlessLoginTokenCacheItem>>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _settingManager = GetRequiredService<ISettingManager>();
        _emailSender = (RecordingEmailSender)GetRequiredService<Volo.Abp.Emailing.IEmailSender>();
        _clock = GetRequiredService<IClock>();
        _connectTokenHandler = GetRequiredService<HttpStubs.RecordingConnectTokenHandler>();
        _tenantManager = GetRequiredService<TenantManager>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private readonly HttpStubs.RecordingConnectTokenHandler _connectTokenHandler;
    private readonly TenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;
    private readonly ICurrentTenant _currentTenant;

    private static int _userSequence;

    private async Task<string> CreateTestUserAsync()
    {
        var email = $"passwordless-{Guid.NewGuid():N}@test.local";
        var userName = $"pl-user-{System.Threading.Interlocked.Increment(ref _userSequence)}";
        var user = new IdentityUser(Guid.NewGuid(), userName, email);
        // 免密登录要求已确认邮箱（硬化后未确认邮箱静默拒绝），测试用户与真实场景对齐
        user.SetEmailConfirmed(true);
        var result = await _userManager.CreateAsync(user, "Test@123456");
        result.Succeeded.ShouldBeTrue(string.Join("; ", result.Errors));
        return email;
    }

    private async Task<PasswordlessLoginTokenCacheItem> SendAndGetCredentialsAsync(string email)
    {
        await _accountProAppService.SendPasswordlessLoginCodeAsync(new SendPasswordlessLoginCodeInput { Email = email });
        var cacheKey = _passwordlessLoginManager.BuildCacheKey(null, email);
        var item = await _passwordlessTokenCache.GetAsync(cacheKey);
        item.ShouldNotBeNull("发码后缓存里应有一次性凭据");
        return item!;
    }

    [Fact]
    public async Task Code_login_consumes_magic_link_and_vice_versa()
    {
        var email = await CreateTestUserAsync();

        // 用码登录成功 → 同一封邮件里的 Magic Link 立即失效
        var first = await SendAndGetCredentialsAsync(email);
        var user = await _passwordlessLoginManager.ValidateAndConsumeAsync(email, first.Code, null);
        user.ShouldNotBeNull();
        user!.Email.ShouldBe(email);

        (await _passwordlessLoginManager.ValidateAndConsumeAsync(email, null, first.MagicLinkToken))
            .ShouldBeNull("用码登录成功后，同一记录的 Magic Link 必须失效");

        // 反向：点链接登录成功 → 验证码立即失效
        var second = await SendAndGetCredentialsAsync(email);
        user = await _passwordlessLoginManager.ValidateAndConsumeAsync(email, null, second.MagicLinkToken);
        user.ShouldNotBeNull();

        (await _passwordlessLoginManager.ValidateAndConsumeAsync(email, second.Code, null))
            .ShouldBeNull("点链接登录成功后，同一记录的验证码必须失效");
    }

    [Fact]
    public async Task Concurrent_Verify_With_Same_Code_Should_Succeed_Exactly_Once()
    {
        var email = await CreateTestUserAsync();
        var credentials = await SendAndGetCredentialsAsync(email);

        // 并发重放同一验证码（与 GDPR DownloadAsync_Concurrent_Replay 同型场景）：
        // ValidateAndConsumeAsync 把「读缓存-比对-删除」收进分布式锁临界区，
        // 先拿到锁的请求命中缓存条目并整条删除（等锁期间 DbContext 也只有赢家在碰），
        // 释放后后来者必然读到缓存未命中。原先无锁的 TOCTOU 写法下
        // 两个请求都能通过比对各自换取登录令牌（一次性凭据被消费两次）。
        var first = _passwordlessLoginManager.ValidateAndConsumeAsync(email, credentials.Code, null);
        var second = _passwordlessLoginManager.ValidateAndConsumeAsync(email, credentials.Code, null);
        await Task.WhenAll(first, second);

        var results = new[] { await first, await second };
        results.Count(r => r != null).ShouldBe(1, "并发重放同一验证码时恰好只能成功一次");
        results.Single(r => r != null)!.Email.ShouldBe(email);
    }

    [Fact]
    public async Task Resend_invalidates_old_credentials()
    {
        var email = await CreateTestUserAsync();

        var oldCredentials = await SendAndGetCredentialsAsync(email);
        var newCredentials = await SendAndGetCredentialsAsync(email);

        newCredentials.Code.ShouldNotBe(oldCredentials.Code, "重发应生成新验证码（极低概率撞码时可重跑）");

        (await _passwordlessLoginManager.ValidateAndConsumeAsync(email, oldCredentials.Code, null))
            .ShouldBeNull("重发后旧验证码必须失效");
        (await _passwordlessLoginManager.ValidateAndConsumeAsync(email, null, oldCredentials.MagicLinkToken))
            .ShouldBeNull("重发后旧 Magic Link 必须失效");

        var user = await _passwordlessLoginManager.ValidateAndConsumeAsync(email, newCredentials.Code, null);
        user.ShouldNotBeNull("新验证码应可正常登录");
    }

    [Fact]
    public async Task Resend_resets_verify_side_rate_limit()
    {
        // 校验侧收紧到 3 次/小时，便于快速打满
        RateLimitingOptions.AddPolicy("EmailVerificationCodeVerify", p => p
            .AddRule(r => r.PartitionByEmail()
                .WithFixedWindow(TimeSpan.FromHours(1), 3)
                .WithMultiTenancy()));

        var email = await CreateTestUserAsync();
        await SendAndGetCredentialsAsync(email);

        // 连续输错 3 次（"000000" 不可能是生成的 6 位码，生成范围 100000-999999）
        for (var i = 0; i < 3; i++)
        {
            (await _passwordlessLoginManager.ValidateAndConsumeAsync(email, "000000", null))
                .ShouldBeNull();
        }

        // 第 4 次触发限流
        await Assert.ThrowsAsync<AbpAdminOperationRateLimitingException>(
            () => _passwordlessLoginManager.ValidateAndConsumeAsync(email, "000000", null));

        // 重发后校验侧限流被重置，可以再次尝试
        await SendAndGetCredentialsAsync(email);
        (await _passwordlessLoginManager.ValidateAndConsumeAsync(email, "000000", null))
            .ShouldBeNull("重发后应允许再次尝试（返回凭据无效而不是限流异常）");
    }

    [Fact]
    public async Task Token_lifetime_is_clamped_to_minimum_30_seconds()
    {
        var email = await CreateTestUserAsync();

        await _settingManager.SetGlobalAsync("AbpAdmin.Account.PasswordlessLoginTokenLifetimeSeconds", "10");
        try
        {
            var beforeSend = _clock.Now;
            var item = await SendAndGetCredentialsAsync(email);

            var effectiveSeconds = (item.ExpiresAt - beforeSend).TotalSeconds;
            effectiveSeconds.ShouldBeGreaterThanOrEqualTo(28, "设 10（低于下限 30）时实际生效值必须是 30 秒");
            effectiveSeconds.ShouldBeLessThanOrEqualTo(32);
        }
        finally
        {
            await _settingManager.SetGlobalAsync("AbpAdmin.Account.PasswordlessLoginTokenLifetimeSeconds", "90");
        }
    }

    [Fact]
    public async Task Email_content_follows_passwordless_mode()
    {
        var email = await CreateTestUserAsync();
        const string modeSetting = "AbpAdmin.Account.PasswordlessLoginMode";

        try
        {
            // 默认 OtpAndMagicLink：一封邮件同时含 6 位验证码与登录链接
            _emailSender.Clear();
            var item = await SendAndGetCredentialsAsync(email);
            _emailSender.SentMessages.Count.ShouldBe(1);
            var body = _emailSender.SentMessages[0].Body!;
            body.ShouldContain(item.Code);
            body.ShouldContain(item.MagicLinkToken);

            // OtpOnly：只有验证码，没有链接
            await _settingManager.SetGlobalAsync(modeSetting, "OtpOnly");
            _emailSender.Clear();
            item = await SendAndGetCredentialsAsync(email);
            body = _emailSender.SentMessages[0].Body!;
            body.ShouldContain(item.Code);
            body.ShouldNotContain(item.MagicLinkToken);

            // MagicLinkOnly：只有链接，没有验证码。
            // 注意：链接 token 是 32 位 hex，理论上可能恰好包含 6 位数字码子串（概率约十万分之三），撞上时重跑即可。
            await _settingManager.SetGlobalAsync(modeSetting, "MagicLinkOnly");
            _emailSender.Clear();
            item = await SendAndGetCredentialsAsync(email);
            body = _emailSender.SentMessages[0].Body!;
            body.ShouldContain(item.MagicLinkToken);
            body.ShouldNotContain(item.Code);
        }
        finally
        {
            await _settingManager.SetGlobalAsync(modeSetting, "OtpAndMagicLink");
        }
    }

    [Fact]
    public async Task Inactive_User_Credentials_Should_Be_Rejected_At_Exchange()
    {
        // round4 fail closed：管理员停用账户后，已送达邮箱的 OTP/magic link 在有效期内
        // 不得再换票——发码侧只查锁定不查 IsActive，状态复核必须发生在消费时刻
        var email = await CreateTestUserAsync();
        var credentials = await SendAndGetCredentialsAsync(email);

        var user = await _userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        user!.SetIsActive(false);
        (await _userManager.UpdateAsync(user)).Succeeded.ShouldBeTrue();

        (await _passwordlessLoginManager.ValidateAndConsumeAsync(email, credentials.Code, null))
            .ShouldBeNull("停用账户不得凭有效 OTP 换票");
    }

    [Fact]
    public async Task Locked_User_Credentials_Should_Be_Rejected_At_Exchange()
    {
        // round4 fail closed：发码后才发生的锁定（如密码侧爆破触发）不应被仍有效的 OTP 绕过
        var email = await CreateTestUserAsync();
        var credentials = await SendAndGetCredentialsAsync(email);

        var user = await _userManager.FindByEmailAsync(email);
        user.ShouldNotBeNull();
        for (var i = 0; i < 10 && !await _userManager.IsLockedOutAsync(user!); i++)
        {
            await _userManager.AccessFailedAsync(user!);
        }
        (await _userManager.IsLockedOutAsync(user!)).ShouldBeTrue("前置条件：连续失败应触发账户锁定");

        (await _passwordlessLoginManager.ValidateAndConsumeAsync(email, credentials.Code, null))
            .ShouldBeNull("锁定账户不得凭有效 OTP 换票");
    }

    // ========== 六透镜审查轮补测：LoginWithMagicLinkAsync 端点 ==========

    [Fact]
    public async Task LoginWithMagicLink_Without_Credentials_Throws_InvalidMagicLink()
    {
        var exception = await Should.ThrowAsync<BusinessException>(
            () => _accountProAppService.LoginWithMagicLinkAsync(
                new LoginWithMagicLinkInput { Email = "someone@test.local" }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.InvalidMagicLink);
    }

    [Fact]
    public async Task LoginWithMagicLink_Unknown_Or_Inactive_Tenant_Throws_InvalidMagicLink()
    {
        _connectTokenHandler.Reset();
        var exception = await Should.ThrowAsync<BusinessException>(
            () => _accountProAppService.LoginWithMagicLinkAsync(
                new LoginWithMagicLinkInput
                {
                    Email = "someone@test.local",
                    MagicLinkToken = "tok",
                    TenantName = "no-such-tenant"
                }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.InvalidMagicLink);
        // fail-closed：租户未解析成功时不得发起换票
        _connectTokenHandler.RecordedRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task LoginWithMagicLink_Forwards_Tenant_Header_And_Credentials_To_Connect_Token()
    {
        // 租户上下文自包含于链接：服务端解析租户名 → 内层 /connect/token 请求必须携带
        // __tenant 头 + grant_type=passwordless + email/magic_link_token（grant 凭此解析
        // 租户级凭据缓存键）
        _connectTokenHandler.Reset();
        var tenant = await _tenantManager.CreateAsync($"pl-t1-{Guid.NewGuid():N}".Substring(0, 20));
        await WithUnitOfWorkAsync(async () => { await _tenantRepository.InsertAsync(tenant); });

        _connectTokenHandler.Enqueue(
            System.Net.HttpStatusCode.OK,
            """{"access_token":"pl-1","token_type":"Bearer","expires_in":90,"refresh_token":"rt-1"}""");

        var result = await _accountProAppService.LoginWithMagicLinkAsync(
            new LoginWithMagicLinkInput
            {
                Email = "tenant-user@t.local",
                MagicLinkToken = "tok-123",
                TenantName = tenant.Name
            });

        result.AccessToken.ShouldBe("pl-1");
        result.RefreshToken.ShouldBe("rt-1");

        var request = _connectTokenHandler.RecordedRequests.ShouldHaveSingleItem();
        request.Form["grant_type"].ShouldBe(AbpAdminOpenIddictDefaults.GrantTypes.Passwordless);
        request.Form["email"].ShouldBe("tenant-user@t.local");
        request.Form["magic_link_token"].ShouldBe("tok-123");
        request.Headers["__tenant"].ShouldBe(tenant.Id.ToString());
    }

    [Fact]
    public async Task LoginWithMagicLink_Exchange_Failure_Masks_As_InvalidMagicLink_Without_Reason()
    {
        // 匿名端点不得透传 OAuth error / 基础设施状态（error.data 会序列化给客户端）
        _connectTokenHandler.Reset();
        _connectTokenHandler.Enqueue(
            System.Net.HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Invalid or expired credentials"}""");

        var exception = await Should.ThrowAsync<BusinessException>(
            () => _accountProAppService.LoginWithMagicLinkAsync(
                new LoginWithMagicLinkInput { Email = "someone@test.local", MagicLinkToken = "tok" }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.InvalidMagicLink);
        exception.Data.Contains("reason").ShouldBeFalse("原因只进服务端日志，不下发匿名调用方");
    }
}
