using System;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Xunit;

namespace AbpAdmin.Account;

/* T2.7 防邮箱枚举集成测试。
 * 覆盖：对不存在邮箱发码响应形状一致（正常返回、无异常）、邮件服务未被调用；
 * 关闭防枚举后未知邮箱会得到明确的业务异常。
 */
public abstract class EmailEnumerationTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IAccountProAppService _accountProAppService;
    private readonly IdentityUserManager _userManager;
    private readonly ISettingManager _settingManager;
    private readonly RecordingEmailSender _emailSender;

    private static int _userSequence;

    protected EmailEnumerationTests()
    {
        _accountProAppService = GetRequiredService<IAccountProAppService>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _settingManager = GetRequiredService<ISettingManager>();
        _emailSender = (RecordingEmailSender)GetRequiredService<Volo.Abp.Emailing.IEmailSender>();
    }

    [Fact]
    public async Task Unknown_email_gets_same_response_shape_and_no_email_sent()
    {
        _emailSender.Clear();

        // 与对存在邮箱的请求形状完全一致：正常返回（void）、不抛异常
        await _accountProAppService.SendPasswordlessLoginCodeAsync(
            new SendPasswordlessLoginCodeInput { Email = $"ghost-{Guid.NewGuid():N}@nowhere.local" });

        _emailSender.SentMessages.ShouldBeEmpty("对未知邮箱不得调用邮件服务");
    }

    [Fact]
    public async Task Known_email_gets_same_response_shape_and_email_sent()
    {
        _emailSender.Clear();
        var email = $"known-{Guid.NewGuid():N}@test.local";
        var userName = $"enum-user-{System.Threading.Interlocked.Increment(ref _userSequence)}";
        var user = new IdentityUser(Guid.NewGuid(), userName, email);
        // 免密登录要求已确认邮箱（硬化后未确认邮箱静默拒绝）
        user.SetEmailConfirmed(true);
        var result = await _userManager.CreateAsync(user, "Test@123456");
        result.Succeeded.ShouldBeTrue();

        await _accountProAppService.SendPasswordlessLoginCodeAsync(
            new SendPasswordlessLoginCodeInput { Email = email });

        _emailSender.SentMessages.Count.ShouldBe(1, "对存在邮箱应发送一封邮件");
        _emailSender.SentMessages[0].To.ShouldBe(email);
    }

    [Fact]
    public async Task Unconfirmed_email_gets_silent_response_and_no_passwordless_email()
    {
        _emailSender.Clear();
        var email = $"unconfirmed-{Guid.NewGuid():N}@test.local";
        var userName = $"enum-user-{System.Threading.Interlocked.Increment(ref _userSequence)}";
        var user = new IdentityUser(Guid.NewGuid(), userName, email);
        // 故意不确认邮箱：会话被劫持者改邮箱后若能直接收无密码登录码，可免密码接管账户
        var result = await _userManager.CreateAsync(user, "Test@123456");
        result.Succeeded.ShouldBeTrue();

        // 响应形状与未知/已确认邮箱一致（正常返回），但不发任何邮件
        await _accountProAppService.SendPasswordlessLoginCodeAsync(
            new SendPasswordlessLoginCodeInput { Email = email });

        _emailSender.SentMessages.ShouldBeEmpty("未确认邮箱不得发送无密码登录凭据");
    }

    [Fact]
    public async Task Unknown_email_confirmation_code_also_sends_nothing()
    {
        _emailSender.Clear();

        await _accountProAppService.SendEmailConfirmationCodeAsync(
            new SendEmailConfirmationCodeInput { Email = $"ghost-{Guid.NewGuid():N}@nowhere.local" });

        _emailSender.SentMessages.ShouldBeEmpty("对未知邮箱不得调用邮件服务");
    }

    [Fact]
    public async Task Disabled_prevention_reveals_unknown_email()
    {
        await _settingManager.SetGlobalAsync("AbpAdmin.Account.PreventEmailEnumeration", "false");
        try
        {
            var exception = await Assert.ThrowsAsync<BusinessException>(
                () => _accountProAppService.SendPasswordlessLoginCodeAsync(
                    new SendPasswordlessLoginCodeInput { Email = $"ghost-{Guid.NewGuid():N}@nowhere.local" }));

            exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.EmailNotRegistered);
        }
        finally
        {
            await _settingManager.SetGlobalAsync("AbpAdmin.Account.PreventEmailEnumeration", "true");
        }
    }
}
