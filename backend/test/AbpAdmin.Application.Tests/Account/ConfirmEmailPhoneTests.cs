using System;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Account;

/* round4 test F2：ConfirmEmailAsync / ConfirmPhoneNumberAsync 此前零覆盖。
 * 这两个匿名端点是可暴力猜码的验证面，且 round3 刚补过防枚举延时——必须锚定：
 * 1. 未知邮箱/手机号与错误验证码返回同一个错误码（响应形状一致，不可枚举已注册账户）
 * 2. 错误验证码不翻转 EmailConfirmed / PhoneNumberConfirmed
 * 3. 正确验证码走通（邮箱：6 位码缓存机制，从录制邮件提取，与真实用户视角一致；手机：Identity SetPhoneNumberConfirmed(true) 落库）
 * 验证码直接用 IdentityUserManager 的 token 生成器产出（与 SendEmailConfirmationCodeAsync
 * 邮件内容同源），不解析邮件正文。
 */
public abstract class ConfirmEmailPhoneTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IAccountProAppService _accountProAppService;
    private readonly IdentityUserManager _userManager;

    private static int _userSequence;

    protected ConfirmEmailPhoneTests()
    {
        _accountProAppService = GetRequiredService<IAccountProAppService>();
        _userManager = GetRequiredService<IdentityUserManager>();
    }

    private async Task<IdentityUser> CreateUserAsync(string prefix)
    {
        var userName = prefix + System.Threading.Interlocked.Increment(ref _userSequence) + "-" + Guid.NewGuid().ToString("N")[..6];
        var user = new IdentityUser(Guid.NewGuid(), userName, userName + "@test.local");
        (await _userManager.CreateAsync(user, "Test@123456")).Succeeded.ShouldBeTrue();
        return user;
    }

    [Fact]
    public async Task ConfirmEmail_Unknown_Email_And_Wrong_Code_Should_Return_Same_Error()
    {
        var user = await CreateUserAsync("ce-");

        // 未知邮箱与错误验证码必须同码：错误码差异会枚举已注册邮箱
        var unknown = await Should.ThrowAsync<BusinessException>(
            () => _accountProAppService.ConfirmEmailAsync(new ConfirmEmailInput
            {
                Email = $"ghost-{Guid.NewGuid():N}@nowhere.local",
                Code = "wrong-code"
            }));
        unknown.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.InvalidEmailConfirmationCode);

        var wrongCode = await Should.ThrowAsync<BusinessException>(
            () => _accountProAppService.ConfirmEmailAsync(new ConfirmEmailInput
            {
                Email = user.Email!,
                Code = "wrong-code"
            }));
        wrongCode.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.InvalidEmailConfirmationCode);

        // 错误验证码不得翻转 EmailConfirmed
        var reloaded = await _userManager.FindByEmailAsync(user.Email!);
        reloaded.ShouldNotBeNull();
        reloaded!.EmailConfirmed.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmEmail_Valid_Code_Should_Confirm()
    {
        // E2E 审查修复后的口径：确认码是 6 位数字（与手机确认同口径），经发码端点
        // 写缓存 + 邮件发出；测试从录制邮件正文提取验证码（与真实用户视角一致）
        var user = await CreateUserAsync("ce-ok-");
        var emailSender = (RecordingEmailSender)GetRequiredService<Volo.Abp.Emailing.IEmailSender>();
        emailSender.Clear();

        await _accountProAppService.SendEmailConfirmationCodeAsync(
            new SendEmailConfirmationCodeInput { Email = user.Email! });

        var sent = emailSender.SentMessages.ShouldHaveSingleItem();
        sent.To.ShouldBe(user.Email);
        var code = System.Text.RegularExpressions.Regex.Match(sent.Body ?? string.Empty, @"\d{6}").Value;
        code.Length.ShouldBe(6, "确认码必须是 6 位数字（前端输入框口径）");

        await _accountProAppService.ConfirmEmailAsync(new ConfirmEmailInput { Email = user.Email!, Code = code });

        var reloaded = await _userManager.FindByEmailAsync(user.Email!);
        reloaded.ShouldNotBeNull();
        reloaded!.EmailConfirmed.ShouldBeTrue();
    }

    [Fact]
    public async Task ConfirmEmail_Code_Is_One_Time_Whatever_Right_Or_Wrong()
    {
        // 一次性语义：错误的尝试也消耗验证码，防同一码反复穷举
        var user = await CreateUserAsync("ce-once-");
        var emailSender = (RecordingEmailSender)GetRequiredService<Volo.Abp.Emailing.IEmailSender>();
        emailSender.Clear();

        await _accountProAppService.SendEmailConfirmationCodeAsync(
            new SendEmailConfirmationCodeInput { Email = user.Email! });
        var code = System.Text.RegularExpressions.Regex.Match(
            emailSender.SentMessages[0].Body ?? string.Empty, @"\d{6}").Value;

        await Should.ThrowAsync<BusinessException>(
            () => _accountProAppService.ConfirmEmailAsync(
                new ConfirmEmailInput { Email = user.Email!, Code = code[..5] + "0" }));

        var second = await Should.ThrowAsync<BusinessException>(
            () => _accountProAppService.ConfirmEmailAsync(
                new ConfirmEmailInput { Email = user.Email!, Code = code }));
        second.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.InvalidEmailConfirmationCode);
    }

    [Fact]
    public async Task ConfirmPhone_Unknown_Number_And_Wrong_Code_Should_Return_Same_Error()
    {
        var user = await CreateUserAsync("cp-");
        const string phoneNumber = "13800138000";
        user.SetPhoneNumber(phoneNumber, false);
        (await _userManager.UpdateAsync(user)).Succeeded.ShouldBeTrue();

        var unknown = await Should.ThrowAsync<BusinessException>(
            () => _accountProAppService.ConfirmPhoneNumberAsync(new ConfirmPhoneNumberInput
            {
                PhoneNumber = "13900139000",
                Code = "000000"
            }));
        unknown.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.InvalidPhoneConfirmationCode);

        var wrongCode = await Should.ThrowAsync<BusinessException>(
            () => _accountProAppService.ConfirmPhoneNumberAsync(new ConfirmPhoneNumberInput
            {
                PhoneNumber = phoneNumber,
                Code = "000000"
            }));
        wrongCode.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.InvalidPhoneConfirmationCode);

        var reloaded = await _userManager.FindByIdAsync(user.Id.ToString());
        reloaded.ShouldNotBeNull();
        reloaded!.PhoneNumberConfirmed.ShouldBeFalse("错误验证码不得翻转 PhoneNumberConfirmed");
    }

    [Fact]
    public async Task ConfirmPhone_Valid_Code_Should_Confirm()
    {
        var user = await CreateUserAsync("cp-ok-");
        const string phoneNumber = "13800138001";
        user.SetPhoneNumber(phoneNumber, false);
        (await _userManager.UpdateAsync(user)).Succeeded.ShouldBeTrue();

        var code = await _userManager.GenerateChangePhoneNumberTokenAsync(user, phoneNumber);
        await _accountProAppService.ConfirmPhoneNumberAsync(new ConfirmPhoneNumberInput
        {
            PhoneNumber = phoneNumber,
            Code = code
        });

        var reloaded = await _userManager.FindByIdAsync(user.Id.ToString());
        reloaded.ShouldNotBeNull();
        reloaded!.PhoneNumberConfirmed.ShouldBeTrue();
    }
}
