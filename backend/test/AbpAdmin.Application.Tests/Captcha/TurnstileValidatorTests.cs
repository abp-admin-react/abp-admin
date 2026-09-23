using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AbpAdmin.HttpStubs;
using AbpAdmin.Settings;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Xunit;

namespace AbpAdmin.Captcha;

/* Turnstile siteverify 声明式客户端（HttpAgent）迁移的回归锚。
 * 覆盖：成功响应放行且请求形状正确（siteverify URL / secret / response 表单）、
 * success=false 抛 CaptchaFailed 并记录 error-codes、5xx 非 JSON 错误体同样 fail-closed
 * （不裸抛）、开关关闭时不发任何 HTTP 请求。
 * 出站请求由 RecordingTurnstileHandler 截停（不真实外呼）。
 */
public abstract class TurnstileValidatorTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly RecordingTurnstileHandler _handler;
    private readonly TurnstileCaptchaValidator _validator;
    private readonly ISettingManager _settingManager;

    protected TurnstileValidatorTests()
    {
        _handler = GetRequiredService<RecordingTurnstileHandler>();
        _validator = GetRequiredService<TurnstileCaptchaValidator>();
        _settingManager = GetRequiredService<ISettingManager>();
        _handler.Reset();
    }

    private async Task EnableCaptchaAsync()
    {
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Account.CaptchaEnabled, "true");
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Account.CaptchaSecretKey, "test-secret");
    }

    [Fact]
    public async Task Valid_response_passes_and_posts_expected_form()
    {
        await EnableCaptchaAsync();
        _handler.Enqueue(HttpStatusCode.OK, """{"success":true}""");

        await _validator.ValidateAsync("tok");

        var request = _handler.RecordedRequests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.RequestUri.ToString()
            .ShouldBe("https://challenges.cloudflare.com/turnstile/v0/siteverify");
        request.Form["secret"].ShouldBe("test-secret");
        request.Form["response"].ShouldBe("tok");
    }

    [Fact]
    public async Task Failed_verification_throws_captcha_failed()
    {
        await EnableCaptchaAsync();
        _handler.Enqueue(HttpStatusCode.OK,
            """{"success":false,"error-codes":["invalid-input-secret"]}""");

        var exception = await Should.ThrowAsync<BusinessException>(() => _validator.ValidateAsync("tok"));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.CaptchaFailed);
    }

    [Fact]
    public async Task Server_error_fails_closed_without_raw_exception()
    {
        // Cloudflare 5xx 返回非 JSON 体：不裸抛解析异常，同样按校验失败处理（fail-closed）
        await EnableCaptchaAsync();
        _handler.Enqueue(HttpStatusCode.InternalServerError, "upstream error", "text/plain");

        var exception = await Should.ThrowAsync<BusinessException>(() => _validator.ValidateAsync("tok"));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.CaptchaFailed);
    }

    [Fact]
    public async Task Enabled_without_secret_throws_not_configured_and_skips_http()
    {
        // 开了开关但没配 secret：CaptchaNotConfigured（而非 CaptchaFailed），且不发任何请求
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Account.CaptchaEnabled, "true");
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Account.CaptchaSecretKey, null);

        var exception = await Should.ThrowAsync<BusinessException>(() => _validator.ValidateAsync("tok"));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.CaptchaNotConfigured);
        _handler.RecordedRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Enabled_with_blank_token_throws_captcha_failed_and_skips_http()
    {
        // 开关开启但 token 为空：CaptchaFailed（round4 审查锚：防未来调整校验顺序翻转异常类型）
        await EnableCaptchaAsync();

        var exception = await Should.ThrowAsync<BusinessException>(() => _validator.ValidateAsync(" "));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.CaptchaFailed);
        _handler.RecordedRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Disabled_switch_skips_http_call_entirely()
    {
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Account.CaptchaEnabled, "false");

        await _validator.ValidateAsync(null); // 开关关闭：token 为空也不校验、不发请求

        _handler.RecordedRequests.ShouldBeEmpty();
    }
}
