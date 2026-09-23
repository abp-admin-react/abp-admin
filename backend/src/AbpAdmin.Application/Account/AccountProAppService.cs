using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Identity;
using AbpAdmin.Permissions;
using AbpAdmin.RateLimiting;
using AbpAdmin.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Account.Settings;
using Volo.Abp.Caching;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Settings;
using Volo.Abp.Users;

namespace AbpAdmin.Account;

/// <summary>
/// T2.7 Account Pro 缺口：邮箱与手机号验证、无密码登录、防枚举、双因素、模拟登录、自助注册开关。
/// </summary>
public class AccountProAppService : AbpAdminAppService, IAccountProAppService
{
    private readonly IdentityUserManager _userManager;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IEmailSender _emailSender;
    private readonly Volo.Abp.Sms.ISmsSender _smsSender;
    private readonly AbpAdmin.Sms.VerificationCodeSmsMessageFactory _smsMessageFactory;
    private readonly ISettingProvider _settingProvider;
    private readonly IOperationRateLimitingChecker _rateLimitingChecker;
    private readonly IDistributedCache<PasswordlessLoginTokenCacheItem> _passwordlessTokenCache;
    private readonly ImpersonationManager _impersonationManager;
    private readonly PasswordlessLoginManager _passwordlessLoginManager;
    private readonly ImpersonationTokenExchanger _tokenExchanger;
    private readonly PasswordlessTokenExchanger _passwordlessTokenExchanger;
    private readonly PasswordlessMagicLinkUrlBuilder _magicLinkUrlBuilder;
    private readonly ITenantStore _tenantStore;
    private readonly ITenantNormalizer _tenantNormalizer;
    private readonly IDistributedCache<EmailConfirmationCodeCacheItem> _emailConfirmationCodeCache;

    /// <summary>
    /// 当前请求的访问令牌（Web 环境由 HttpApi.Host 的实现注入；测试环境未注册时保持 Null 实现）。
    /// 属性注入与 ABP 的 Logger 同一模式：容器里没有该服务时保留默认值，不抛异常。
    /// </summary>
    public ICurrentAccessTokenProvider AccessTokenProvider { get; set; } = new NullCurrentAccessTokenProvider();

    public AccountProAppService(
        IdentityUserManager userManager,
        IIdentityUserRepository userRepository,
        IEmailSender emailSender,
        Volo.Abp.Sms.ISmsSender smsSender,
        AbpAdmin.Sms.VerificationCodeSmsMessageFactory smsMessageFactory,
        ISettingProvider settingProvider,
        IOperationRateLimitingChecker rateLimitingChecker,
        IDistributedCache<PasswordlessLoginTokenCacheItem> passwordlessTokenCache,
        ImpersonationManager impersonationManager,
        PasswordlessLoginManager passwordlessLoginManager,
        ImpersonationTokenExchanger tokenExchanger,
        PasswordlessTokenExchanger passwordlessTokenExchanger,
        PasswordlessMagicLinkUrlBuilder magicLinkUrlBuilder,
        ITenantStore tenantStore,
        ITenantNormalizer tenantNormalizer,
        IDistributedCache<EmailConfirmationCodeCacheItem> emailConfirmationCodeCache)
    {
        _userManager = userManager;
        _userRepository = userRepository;
        _emailSender = emailSender;
        _smsSender = smsSender;
        _smsMessageFactory = smsMessageFactory;
        _settingProvider = settingProvider;
        _rateLimitingChecker = rateLimitingChecker;
        _passwordlessTokenCache = passwordlessTokenCache;
        _impersonationManager = impersonationManager;
        _passwordlessLoginManager = passwordlessLoginManager;
        _tokenExchanger = tokenExchanger;
        _passwordlessTokenExchanger = passwordlessTokenExchanger;
        _magicLinkUrlBuilder = magicLinkUrlBuilder;
        _tenantStore = tenantStore;
        _tenantNormalizer = tenantNormalizer;
        _emailConfirmationCodeCache = emailConfirmationCodeCache;
    }

    /// <summary>
    /// 未知或锁定邮箱的统一处理：PreventEmailEnumeration 开启（默认）时模拟正常路径耗时静默返回，
    /// 不发送任何邮件，响应形状与正常请求一致；关闭时抛出业务异常直接告知邮箱未注册。
    /// </summary>
    private async Task HandleUnknownEmailAsync(string email, string operation)
    {
        var preventEnumeration = await _settingProvider.IsTrueAsync(AbpAdminSettings.Account.PreventEmailEnumeration);
        if (!preventEnumeration)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.EmailNotRegistered);
        }

        await AccountAntiEnumeration.DelayAsync();
        Logger.LogWarning("{Operation}: 未知或锁定邮箱 {Email}，防枚举模式返回", operation, email);
    }

    // ========== 1. 邮箱与手机号验证 ==========

    [AllowAnonymous]
    public virtual async Task SendEmailConfirmationCodeAsync(SendEmailConfirmationCodeInput input)
    {
        // 防枚举：先查用户，不存在或锁定时按设置决定静默返回还是报错
        var user = await _userRepository.FindByNormalizedEmailAsync(input.Email.ToUpperInvariant());
        var isValidUser = user != null && !await _userManager.IsLockedOutAsync(user);

        // 限流：发码策略
        await _rateLimitingChecker.CheckAsync(OperationRateLimitingPolicyNames.EmailVerificationCode, input.Email);

        if (!isValidUser)
        {
            await HandleUnknownEmailAsync(input.Email, nameof(SendEmailConfirmationCodeAsync));
            return;
        }

        // 生成 6 位数字确认码入缓存（E2E 审查修复：原先用 Identity 的
        // GenerateEmailConfirmationTokenAsync 产出 200+ 字符 SecureToken 当「确认码」，
        // 与前端 6 位输入框语义不匹配；改与手机号确认同口径——6 位数字码，10 分钟有效。
        // 缓存只存哈希；验码侧限流（5 次/10 分钟）约束爆破面）
        var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var cacheKey = BuildEmailConfirmationCacheKey(input.Email);
        await _emailConfirmationCodeCache.RemoveAsync(cacheKey);
        await _emailConfirmationCodeCache.SetAsync(
            cacheKey,
            new EmailConfirmationCodeCacheItem
            {
                CodeHash = EmailConfirmationCodeCacheItem.Hash(code, CurrentTenant.Id, input.Email),
                ExpiresAt = Clock.Now.AddMinutes(10)
            },
            new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10)
            });

        // 发送邮件
        await _emailSender.SendAsync(
            input.Email,
            L["EmailConfirmationCodeSubject"],
            L["EmailConfirmationCodeBody", code]
        );

        // 发新码重置校验侧限流：用户重新要码，上一轮失败尝试不再计入
        await _rateLimitingChecker.ResetAsync(OperationRateLimitingPolicyNames.EmailVerificationCodeVerify, input.Email);

        Logger.LogInformation("邮箱确认验证码已发送至 {Email}", input.Email);
    }

    [AllowAnonymous]
    public virtual async Task SendPhoneNumberConfirmationCodeAsync(SendPhoneNumberConfirmationCodeInput input)
    {
        // 服务端过滤（GetListAsync 带 phoneNumber 命名参数），避免匿名端点把整个用户表载入内存
        var users = await _userRepository.GetListAsync(phoneNumber: input.PhoneNumber);
        var user = users.FirstOrDefault();
        var isValidUser = user != null && !await _userManager.IsLockedOutAsync(user);

        // 发码侧限流已收口到 T3.5 的 SmsSenderResolver（所有短信路径全覆盖），这里不再重复计数，
        // 否则有效用户每发一条会被记两次（本方法一次、resolver 一次）。
        // 未注册手机号的探测不再消耗发码配额——反正对它们也不会真发短信。

        if (!isValidUser)
        {
            await AccountAntiEnumeration.DelayAsync();
            Logger.LogWarning("SendPhoneNumberConfirmationCodeAsync: 未知或锁定手机号 {PhoneNumber}，防枚举模式返回", input.PhoneNumber);
            return;
        }

        var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user!, input.PhoneNumber);

        // T3.5：经 SmsSenderResolver 发送（厂商/凭据走 SettingUi；未配置厂商时日志记录不抛异常）
        await _smsSender.SendAsync(await _smsMessageFactory.BuildAsync(input.PhoneNumber, code));

        // 发新码重置校验侧限流：用户重新要码，上一轮失败尝试不再计入
        await _rateLimitingChecker.ResetAsync(OperationRateLimitingPolicyNames.SmsVerificationCodeVerify, input.PhoneNumber);

        Logger.LogInformation("手机号确认验证码已发送，手机号 {PhoneNumber}", input.PhoneNumber);
    }

    [AllowAnonymous]
    public virtual async Task ConfirmEmailAsync(ConfirmEmailInput input)
    {
        // 校验侧独立限流（防暴力猜码），与发码侧策略分开
        await _rateLimitingChecker.CheckAsync(OperationRateLimitingPolicyNames.EmailVerificationCodeVerify, input.Email);

        var user = await _userRepository.FindByNormalizedEmailAsync(input.Email.ToUpperInvariant());
        if (user == null)
        {
            // 未知用户即时抛错、已知用户要先走 token 哈希校验——响应耗时差可枚举已注册邮箱，
            // 与发码侧同法补防枚举延时（round3 security lens）
            await AccountAntiEnumeration.DelayAsync();
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidEmailConfirmationCode);
        }

        // 校验缓存中的 6 位码（一次性：无论对错都删，防同一码穷举；置位方式对齐手机确认）
        var cacheKey = BuildEmailConfirmationCacheKey(input.Email);
        var cacheItem = await _emailConfirmationCodeCache.GetAsync(cacheKey);
        await _emailConfirmationCodeCache.RemoveAsync(cacheKey);
        if (cacheItem == null || !cacheItem.IsValid(CurrentTenant.Id, input.Email, input.Code, Clock.Now))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidEmailConfirmationCode);
        }

        user.SetEmailConfirmed(true);
        (await _userManager.UpdateAsync(user)).CheckErrors();

        // 校验成功后重置限流
        await _rateLimitingChecker.ResetAsync(OperationRateLimitingPolicyNames.EmailVerificationCodeVerify, input.Email);

        Logger.LogInformation("邮箱 {Email} 确认成功", input.Email);
    }

    [AllowAnonymous]
    public virtual async Task ConfirmPhoneNumberAsync(ConfirmPhoneNumberInput input)
    {
        await _rateLimitingChecker.CheckAsync(OperationRateLimitingPolicyNames.SmsVerificationCodeVerify, input.PhoneNumber);

        // 服务端过滤（GetListAsync 带 phoneNumber 命名参数），避免匿名端点把整个用户表载入内存
        var users = await _userRepository.GetListAsync(phoneNumber: input.PhoneNumber);
        var user = users.FirstOrDefault();
        if (user == null)
        {
            // 同邮箱确认：耗时对齐防枚举（round3 security lens）
            await AccountAntiEnumeration.DelayAsync();
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidPhoneConfirmationCode);
        }

        var result = await _userManager.VerifyChangePhoneNumberTokenAsync(user, input.Code, input.PhoneNumber);
        if (!result)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidPhoneConfirmationCode);
        }

        // 确认手机号
        user.SetPhoneNumberConfirmed(true);
        await _userManager.UpdateAsync(user);

        await _rateLimitingChecker.ResetAsync(OperationRateLimitingPolicyNames.SmsVerificationCodeVerify, input.PhoneNumber);

        Logger.LogInformation("手机号 {PhoneNumber} 确认成功", input.PhoneNumber);
    }

    // ========== 2. 无密码登录（验证码 / Magic Link） ==========

    [AllowAnonymous]
    public virtual async Task SendPasswordlessLoginCodeAsync(SendPasswordlessLoginCodeInput input)
    {
        var mode = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Account.PasswordlessLoginMode)
                   ?? "OtpAndMagicLink";

        var user = await _userRepository.FindByNormalizedEmailAsync(input.Email.ToUpperInvariant());
        // 硬化（Pro 对齐）：未确认邮箱不放行无密码登录。改邮箱走 SetEmailAsync 会把
        // EmailConfirmed 重置为 false——若放行，会话被劫持者把邮箱改成自己的即可
        // 免密码永久接管账户（无密码登录不验密码）。未确认口径与未知邮箱一致静默返回
        //（防枚举），用户先走密码登录在个人中心确认邮箱后再用无密码登录。
        var isValidUser = user != null
                          && !await _userManager.IsLockedOutAsync(user)
                          && user.EmailConfirmed;

        await _rateLimitingChecker.CheckAsync(OperationRateLimitingPolicyNames.EmailVerificationCode, input.Email);

        if (!isValidUser)
        {
            if (user != null)
            {
                Logger.LogWarning(
                    "SendPasswordlessLoginCodeAsync: 邮箱 {Email} 未确认或已锁定，按防枚举口径静默返回", input.Email);
            }
            await HandleUnknownEmailAsync(input.Email, nameof(SendPasswordlessLoginCodeAsync));
            return;
        }

        // 读取 token 有效期（秒），钳制到边界（默认值与边界见 AbpAdminAccountConsts）
        var lifetimeSeconds = await GetClampedLifetimeSecondsAsync(
            AbpAdminSettings.Account.PasswordlessLoginTokenLifetimeSeconds,
            AbpAdminAccountConsts.PasswordlessTokenLifetimeDefaultSeconds,
            AbpAdminAccountConsts.PasswordlessTokenLifetimeMinSeconds,
            AbpAdminAccountConsts.PasswordlessTokenLifetimeMaxSeconds);

        // 生成一次性凭据：验证码 + Magic Link token 共享同一条缓存记录。
        // code 用 RandomNumberGenerator（CSPRNG）：Random.Shared 是 Mersenne Twister，
        // 输出理论上可反推内部状态，安全敏感凭据不允许用；上界同时修正为覆盖全部 6 位数
        //（Random.Next 上界开区间，原写法永远取不到 999999）。
        // magic link token 的 Guid.NewGuid 在 .NET 上即由 CSPRNG 生成（v4 随机 Guid），保持不变。
        var cacheKey = _passwordlessLoginManager.BuildCacheKey(CurrentTenant.Id, input.Email);
        var code = System.Security.Cryptography.RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var magicLinkToken = Guid.NewGuid().ToString("N");

        var cacheItem = new PasswordlessLoginTokenCacheItem
        {
            Email = input.Email,
            Code = code,
            MagicLinkToken = magicLinkToken,
            ExpiresAt = Clock.Now.AddSeconds(lifetimeSeconds)
        };

        // 重发：先删旧凭据，再写新凭据，并重置校验侧限流（用户重新要码，上一轮失败尝试不再计入）
        await _passwordlessTokenCache.RemoveAsync(cacheKey);
        await _passwordlessTokenCache.SetAsync(cacheKey, cacheItem, new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
        {
            AbsoluteExpiration = cacheItem.ExpiresAt
        });
        await _rateLimitingChecker.ResetAsync(OperationRateLimitingPolicyNames.EmailVerificationCodeVerify, input.Email);

        // 根据模式发送邮件
        var sendOtp = mode is "OtpAndMagicLink" or "OtpOnly";
        var sendMagicLink = mode is "OtpAndMagicLink" or "MagicLinkOnly";

        if (sendOtp || sendMagicLink)
        {
            // Magic Link 指向 SPA 前端（绝对地址，邮件客户端可直接点开）；
            // 租户邮件把租户名编码进链接，落地页据此自建租户上下文
            string? magicLinkUrl = null;
            if (sendMagicLink)
            {
                var tenantName = CurrentTenant.Id == null
                    ? null
                    : (await _tenantStore.FindAsync(CurrentTenant.Id.Value))?.Name;
                magicLinkUrl = _magicLinkUrlBuilder.BuildLoginUrl(magicLinkToken, input.Email, tenantName);
            }

            var body = BuildPasswordlessEmailBody(
                L["PasswordlessLoginBody"].Value, code, magicLinkUrl, sendOtp, sendMagicLink, lifetimeSeconds);
            await _emailSender.SendAsync(input.Email, L["PasswordlessLoginSubject"].Value, body);
        }

        Logger.LogInformation("无密码登录凭据已发送至 {Email}，模式：{Mode}", input.Email, mode);
    }

    /// <summary>
    /// 消费 Magic Link（或邮件验证码兜底）换取登录令牌。匿名端点，凭据一次性语义由
    /// grant 内的 PasswordlessLoginManager 保证。前端落地页在 /user/login 检测链接参数后调用。
    /// </summary>
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public virtual async Task<ImpersonationResultDto> LoginWithMagicLinkAsync(LoginWithMagicLinkInput input)
    {
        // 凭据二选一校验收在服务端（Contracts 层不做 IValidatableObject 硬编码文案），
        // 与本端点其余失败路径共用 InvalidMagicLink 错误码
        if (input.MagicLinkToken.IsNullOrWhiteSpace() && input.Code.IsNullOrWhiteSpace())
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidMagicLink);
        }

        // 租户上下文自包含于链接（tenant 参数），不依赖调用方的 __tenant 头——
        // 用户从邮件打开链接时本地没有任何租户状态
        Guid? tenantId = null;
        if (!input.TenantName.IsNullOrWhiteSpace())
        {
            // 租户名经 ITenantNormalizer 归一化后再查（TenantStore 按 NormalizedName 精确匹配；
            // 走框架归一化点而非手写 ToUpperInvariant，与 by-name 端点共用同一策略）
            var tenant = await _tenantStore.FindAsync(_tenantNormalizer.NormalizeName(input.TenantName));
            if (tenant == null || !tenant.IsActive)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidMagicLink);
            }

            tenantId = tenant.Id;
        }

        using (CurrentTenant.Change(tenantId))
        {
            var cancellationToken = GetRequestAbortedOrNone();

            return await _passwordlessTokenExchanger.ExchangeAsync(
                input.Email, input.Code, input.MagicLinkToken, tenantId, cancellationToken);
        }
    }

    /// <summary>邮箱确认码缓存键：租户 + 大写邮箱（发码与验码同键）。</summary>
    private string BuildEmailConfirmationCacheKey(string email)
    {
        return $"email-confirmation:{CurrentTenant.Id?.ToString() ?? "host"}:{email.ToUpperInvariant()}";
    }

    /// <summary>
    /// 读取秒数设置并钳制到 [min, max]，无法解析或缺失时返回默认值。
    /// </summary>
    private async Task<int> GetClampedLifetimeSecondsAsync(string settingName, int defaultValue, int min, int max)
    {
        var raw = await _settingProvider.GetOrNullAsync(settingName);
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, out var parsed))
        {
            if (parsed < min || parsed > max)
            {
                Logger.LogWarning("{SettingName} 值 {Value} 超出范围 [{Min}, {Max}]，已钳制", settingName, parsed, min, max);
                return Math.Clamp(parsed, min, max);
            }

            return parsed;
        }

        return defaultValue;
    }

    /// <summary>
    /// 无密码登录邮件正文：按模式替换 {Code} / {MagicLinkUrl} / {LifetimeSeconds} 占位符
    /// （不发送的部分替换为空串）。MagicLinkUrl 由 <see cref="PasswordlessMagicLinkUrlBuilder"/> 构造的绝对地址。
    /// </summary>
    private static string BuildPasswordlessEmailBody(
        string template,
        string code,
        string? magicLinkUrl,
        bool sendOtp,
        bool sendMagicLink,
        int lifetimeSeconds)
    {
        var body = template;

        body = sendOtp
            ? body.Replace("{Code}", code)
            : body.Replace("{Code}", string.Empty);

        body = body.Replace("{MagicLinkUrl}", sendMagicLink ? magicLinkUrl ?? string.Empty : string.Empty);
        body = body.Replace("{LifetimeSeconds}", lifetimeSeconds.ToString());

        return body;
    }

    // ========== 3. 双因素认证 ==========

    [Authorize]
    public virtual async Task<TwoFactorStatusDto> GetTwoFactorStatusAsync()
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        return new TwoFactorStatusDto
        {
            TwoFactorEnabled = user.TwoFactorEnabled,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed
        };
    }

    [Authorize]
    public virtual async Task SetTwoFactorEnabledAsync(SetTwoFactorEnabledInput input)
    {
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());

        // 启用 2FA 前必须至少有一个已确认的联系方式（领域前提单出处：TwoFactorGuard，管理端同源）
        if (input.Enabled)
        {
            Identity.TwoFactorGuard.EnsureCanEnable(user);
        }

        // 关闭 2FA 必须先验证一次双因素验证码（启用不验：加保护无需确认，撤保护必须本人确认），
        // 与 AccountSecurityAppService.DisableAuthenticatorAsync 的验码语义对齐
        if (!input.Enabled)
        {
            await VerifyTwoFactorCodeInternalAsync(user, input.Code, input.Provider);
        }

        // 使用 UserManager 的 SetTwoFactorEnabledAsync 方法
        var result = await _userManager.SetTwoFactorEnabledAsync(user, input.Enabled);
        if (!result.Succeeded)
        {
            throw new BusinessException("AbpAdmin:SetTwoFactorEnabledFailed")
                .WithData("Errors", string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        Logger.LogInformation("用户 {UserId} 双因素认证已{Action}", user.Id, input.Enabled ? "启用" : "禁用");
    }

    [Authorize]
    public virtual async Task SendTwoFactorCodeAsync(SendTwoFactorCodeInput input)
    {
        // 只允许给当前登录用户自己发码：入参不携带 UserId，防止对任意用户触发验证码（骚扰/钓鱼/消耗限流配额）
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        // 通道解析刻意不含 Authenticator：验证器码在本地生成、无需发送，
        // 发送侧仅覆盖 Email/Phone 两条「需要投递」的通道（验证器兜底只在验码侧，见 VerifyTwoFactorCodeInternalAsync）
        var provider = input.Provider ?? (user.EmailConfirmed ? "Email" : user.PhoneNumberConfirmed ? "Phone" : null);

        if (provider == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.TwoFactorNoConfirmedProvider);
        }

        await _rateLimitingChecker.CheckAsync(OperationRateLimitingPolicyNames.TwoFactorCode, user.Id.ToString());

        string code;
        if (provider == "Email")
        {
            code = await _userManager.GenerateTwoFactorTokenAsync(user, "Email");
            await _emailSender.SendAsync(user.Email!, L["TwoFactorCodeSubject"], L["TwoFactorCodeBody", code]);
        }
        else
        {
            code = await _userManager.GenerateTwoFactorTokenAsync(user, "Phone");
            // T3.5：经 SmsSenderResolver 发送（厂商/凭据走 SettingUi；未配置厂商时日志记录不抛异常）
            await _smsSender.SendAsync(await _smsMessageFactory.BuildAsync(user.PhoneNumber!, code));
        }

        // 发新码重置校验侧限流：用户重新要码，上一轮失败尝试不再计入
        await _rateLimitingChecker.ResetAsync(OperationRateLimitingPolicyNames.TwoFactorCodeVerify, user.Id.ToString());

        Logger.LogInformation("双因素验证码已发送，用户 {UserId}，通道 {Provider}", user.Id, provider);
    }

    [Authorize]
    public virtual async Task VerifyTwoFactorCodeAsync(VerifyTwoFactorCodeInput input)
    {
        // 只允许校验当前登录用户自己的验证码（与 SendTwoFactorCodeAsync 对应）
        var user = await _userManager.GetByIdAsync(CurrentUser.GetId());
        await VerifyTwoFactorCodeInternalAsync(user, input.Code, input.Provider);

        Logger.LogInformation("双因素验证通过，用户 {UserId}", user.Id);
    }

    /// <summary>
    /// 双因素验证码校验公共实现（VerifyTwoFactorCodeAsync 与关闭 2FA 共用）：
    /// 通道解析 → 校验侧限流 → token 校验 → 成功重置限流。
    /// </summary>
    private async Task VerifyTwoFactorCodeInternalAsync(
        Volo.Abp.Identity.IdentityUser user, string? code, string? provider)
    {
        // 通道解析:邮箱 → 手机 → 验证器(E2E 审查修复:仅启用验证器的用户在关闭 2FA 时
        // 没有已确认邮箱/手机可收码,提供 Authenticator 兜底,否则该人群无法走完关闭确认流)
        var resolved = provider
                       ?? (user.EmailConfirmed ? "Email" : null)
                       ?? (user.PhoneNumberConfirmed ? "Phone" : null)
                       ?? ((await _userManager.GetValidTwoFactorProvidersAsync(user))
                           .Contains(TokenOptions.DefaultAuthenticatorProvider)
                               ? TokenOptions.DefaultAuthenticatorProvider
                               : null);

        if (resolved == null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.TwoFactorNoConfirmedProvider);
        }

        if (code.IsNullOrWhiteSpace())
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidTwoFactorCode);
        }

        await _rateLimitingChecker.CheckAsync(OperationRateLimitingPolicyNames.TwoFactorCodeVerify, user.Id.ToString());

        var isValid = await _userManager.VerifyTwoFactorTokenAsync(user, resolved, code);
        if (!isValid)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.InvalidTwoFactorCode);
        }

        await _rateLimitingChecker.ResetAsync(OperationRateLimitingPolicyNames.TwoFactorCodeVerify, user.Id.ToString());
    }

    // ========== 4. 模拟登录 ==========

    [Authorize(AbpAdminPermissions.Impersonation.Tenant)]
    public virtual Task<ImpersonationResultDto> ImpersonateTenantAsync(ImpersonateTenantInput input)
    {
        // 目标解析与 claim 构建由 /connect/token 的 impersonation 扩展授权完成（grant 内复查权限），
        // 这里做权限把关并转发当前令牌完成交换。
        return ExchangeImpersonationTokenAsync(new Dictionary<string, string>
        {
            ["tenant_id"] = input.TenantId.ToString()
        });
    }

    [Authorize(AbpAdminPermissions.Impersonation.User)]
    public virtual Task<ImpersonationResultDto> ImpersonateUserAsync(ImpersonateUserInput input)
    {
        return ExchangeImpersonationTokenAsync(new Dictionary<string, string>
        {
            ["user_id"] = input.UserId.ToString()
        });
    }

    [Authorize]
    public virtual async Task<ImpersonationResultDto> BackToMyAccountAsync()
    {
        // 必须先校验当前令牌确实带 impersonator claim（无则 403），否则任何人都能调此端点提权。
        // 该检查在 HTTP 交换之前，集成测试无需 Web 环境即可覆盖。
        await _impersonationManager.GetImpersonatorUserOrThrowAsync();

        return await ExchangeImpersonationTokenAsync(new Dictionary<string, string>());
    }

    [Authorize]
    public virtual Task<ImpersonationResultDto> StartDelegationAsync(Guid delegationId)
    {
        return ExchangeImpersonationTokenAsync(new Dictionary<string, string>
        {
            ["delegation_id"] = delegationId.ToString()
        });
    }

    /// <summary>
    /// 向 /connect/token 发起 impersonation 扩展授权交换（HTTP 细节见 <see cref="ImpersonationTokenExchanger"/>）。
    /// 转发当前访问令牌，grant 据此识别当前用户并复查模拟登录权限。
    /// </summary>
    private async Task<ImpersonationResultDto> ExchangeImpersonationTokenAsync(Dictionary<string, string> extraParameters)
    {
        var cancellationToken = GetRequestAbortedOrNone();

        // 直接返回 Exchanger 构造好的 DTO（round4 审查 R1：原字段逐一拷贝的中间映射已删）
        return await _tokenExchanger.ExchangeAsync(
            AccessTokenProvider.GetAccessToken(),
            extraParameters,
            CurrentUser.FindClaimValue("client_id"),
            cancellationToken);
    }

    // ========== 5. 自助注册开关 ==========

    [AllowAnonymous]
    public virtual async Task<bool> IsSelfRegistrationEnabledAsync()
    {
        return await _settingProvider.IsTrueAsync(AccountSettingNames.IsSelfRegistrationEnabled);
    }
}
