using System;
using System.Threading.Tasks;
using AbpAdmin.Account;
using AbpAdmin.Captcha;
using AbpAdmin.Settings;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Xunit;

namespace AbpAdmin.Captcha;

/* 自托管图形验证码（对标 RuoYi 登录验证码）测试。
 * 覆盖：取图端点返回 PNG data URL；答案哈希存储 + 一次性消费（对/错/重放/大小写/空白）；
 * LoginCaptchaManager 的提供者矩阵（默认关、Image 生效、Turnstile 缺 SiteKey 视为关）。
 * 设置写入走 ISettingManager.SetGlobalAsync（与 EmailEnumerationTests 同款基建）。
 */
public abstract class ImageCaptchaTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly CaptchaImageAppService _captchaImageAppService;
    private readonly ImageCaptchaManager _imageCaptchaManager;
    private readonly LoginCaptchaManager _loginCaptchaManager;
    private readonly ISettingManager _settingManager;

    protected ImageCaptchaTests()
    {
        _captchaImageAppService = GetRequiredService<CaptchaImageAppService>();
        _imageCaptchaManager = GetRequiredService<ImageCaptchaManager>();
        _loginCaptchaManager = GetRequiredService<LoginCaptchaManager>();
        _settingManager = GetRequiredService<ISettingManager>();
    }

    private async Task ResetCaptchaSettingsAsync()
    {
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Account.CaptchaEnabled, "false");
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Account.CaptchaProvider, "Turnstile");
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Account.CaptchaSiteKey, null);
    }

    [Fact]
    public async Task Generate_Should_Return_Png_DataUrl()
    {
        var dto = await _captchaImageAppService.GetAsync();

        dto.Id.ShouldNotBe(Guid.Empty);
        dto.ImageDataUrl.ShouldStartWith("data:image/png;base64,");
        var bytes = Convert.FromBase64String(dto.ImageDataUrl["data:image/png;base64,".Length..]);
        bytes.Length.ShouldBeGreaterThan(100);
        // PNG magic bytes
        bytes[0].ShouldBe((byte)0x89);
        bytes[1].ShouldBe((byte)'P');
        bytes[2].ShouldBe((byte)'N');
        bytes[3].ShouldBe((byte)'G');
    }

    [Fact]
    public async Task Validate_Should_Consume_Once_And_Ignore_Case()
    {
        var id = Guid.NewGuid();
        await _imageCaptchaManager.RememberAsync(id, "AB3D");

        // 大小写不敏感 + 首尾空白容忍
        (await _imageCaptchaManager.ValidateAndConsumeAsync(id, " ab3d ")).ShouldBeTrue();

        // 一次性：同一张图不能复用（防穷举）
        (await _imageCaptchaManager.ValidateAndConsumeAsync(id, "AB3D")).ShouldBeFalse();
    }

    [Fact]
    public async Task Validate_Wrong_Code_Or_Unknown_Id_Should_Fail()
    {
        var id = Guid.NewGuid();
        await _imageCaptchaManager.RememberAsync(id, "AB3D");

        (await _imageCaptchaManager.ValidateAndConsumeAsync(id, "WRONG")).ShouldBeFalse();
        // 错误尝试同样消费
        (await _imageCaptchaManager.ValidateAndConsumeAsync(id, "AB3D")).ShouldBeFalse();

        (await _imageCaptchaManager.ValidateAndConsumeAsync(Guid.NewGuid(), "AB3D")).ShouldBeFalse();
    }

    [Fact]
    public async Task Provider_Matrix_Should_Follow_Settings()
    {
        await ResetCaptchaSettingsAsync();

        // 默认关：GetProvider 为 null，Validate 直接放行
        (await _loginCaptchaManager.GetProviderAsync()).ShouldBeNull();
        await _loginCaptchaManager.ValidateAsync(null, null, null); // 不抛

        // Turnstile 缺 SiteKey：视为关
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Account.CaptchaEnabled, "true");
        (await _loginCaptchaManager.GetProviderAsync()).ShouldBeNull();

        // Image 提供者：生效，缺答案必须拒绝
        await _settingManager.SetGlobalAsync(AbpAdminSettings.Account.CaptchaProvider, "Image");
        (await _loginCaptchaManager.GetProviderAsync()).ShouldBe(LoginCaptchaManager.ProviderImage);
        (await Should.ThrowAsync<BusinessException>(() =>
                _loginCaptchaManager.ValidateAsync(null, null, null)))
            .Code.ShouldBe(AbpAdminDomainErrorCodes.Account.CaptchaFailed);

        // Image 提供者：正确答案放行
        var id = Guid.NewGuid();
        await _imageCaptchaManager.RememberAsync(id, "AB3D");
        await _loginCaptchaManager.ValidateAsync(null, id, "ab3d");

        // 还原，避免污染其他测试
        await ResetCaptchaSettingsAsync();
    }
}
