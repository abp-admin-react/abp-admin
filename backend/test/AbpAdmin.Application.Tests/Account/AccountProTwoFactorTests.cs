using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace AbpAdmin.Account;

/* AccountPro 双因素开关的自服务面测试（六透镜审查轮补测）。
 * 既有 SetTwoFactorEnabledAsync 测试测的是 IIdentityUserAdminAppService（管理端，不验码）；
 * 本文件钉住自服务语义：启用不需验码（加保护无需确认），关闭必须先验一次双因素验证码
 * （撤保护必须本人确认，与会话劫持者关闭保护对抗）。
 * 验证码经 Identity 的 Email token provider（测试模块已注册默认 providers + 易失 DataProtection）。
 */
public abstract class AccountProTwoFactorTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IAccountProAppService _accountProAppService;
    private readonly IdentityUserManager _userManager;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;

    private static int _userSequence;

    protected AccountProTwoFactorTests()
    {
        _accountProAppService = GetRequiredService<IAccountProAppService>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _currentPrincipalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    /// <summary>创建已确认邮箱的用户（自服务语义：发码/校验对象固定当前用户）。</summary>
    private async Task<IdentityUser> CreateConfirmedUserAsync()
    {
        var seq = System.Threading.Interlocked.Increment(ref _userSequence);
        var user = new IdentityUser(Guid.NewGuid(), $"tf-user-{seq}", $"tf-user-{seq}@test.local");
        (await _userManager.CreateAsync(user, "Test@123456")).Succeeded.ShouldBeTrue();
        user.SetEmailConfirmed(true);
        (await _userManager.UpdateAsync(user)).Succeeded.ShouldBeTrue();
        return user;
    }

    /// <summary>以指定用户身份执行服务调用。作用域必须在测试方法内同步创建
    /// （测试默认主体是库中不存在的固定 host admin；AsyncLocal 不能跨 await 边界建立）。</summary>
    private IDisposable AsUser(IdentityUser user)
    {
        return _currentPrincipalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, user.Id.ToString()),
            new Claim(AbpClaimTypes.UserName, user.UserName),
            new Claim(AbpClaimTypes.Email, user.Email),
            new Claim(AbpClaimTypes.EmailVerified, "true")
        })));
    }

    [Fact]
    public async Task Enable_Without_Code_Still_Succeeds()
    {
        var user = await CreateConfirmedUserAsync();
        using (AsUser(user))
        {
            // 启用不验码：加保护无需确认（防有人把验码条件写反到 enable 侧）
            await _accountProAppService.SetTwoFactorEnabledAsync(new SetTwoFactorEnabledInput { Enabled = true });

            var refreshed = await _userManager.GetByIdAsync(user.Id);
            refreshed.TwoFactorEnabled.ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Disable_Without_Code_Throws_And_Keeps_Enabled()
    {
        var user = await CreateConfirmedUserAsync();
        using (AsUser(user))
        {
            await _accountProAppService.SetTwoFactorEnabledAsync(new SetTwoFactorEnabledInput { Enabled = true });

            var exception = await Should.ThrowAsync<BusinessException>(
                () => _accountProAppService.SetTwoFactorEnabledAsync(
                    new SetTwoFactorEnabledInput { Enabled = false, Code = null }));

            exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.InvalidTwoFactorCode);

            var refreshed = await _userManager.GetByIdAsync(user.Id);
            refreshed.TwoFactorEnabled.ShouldBeTrue("验码失败/未提供时不得关闭保护");
        }
    }

    [Fact]
    public async Task Disable_With_Valid_Email_Code_Disables()
    {
        var user = await CreateConfirmedUserAsync();
        using (AsUser(user))
        {
            await _accountProAppService.SetTwoFactorEnabledAsync(new SetTwoFactorEnabledInput { Enabled = true });

            // 启用 2FA 会重置 SecurityStamp（Identity UpdateSecurityStampInternal）：
            // 必须重取实体生成验证码，否则令牌绑的是旧 stamp，服务端校验必失败
            var fresh = await _userManager.GetByIdAsync(user.Id);
            var code = await _userManager.GenerateTwoFactorTokenAsync(fresh, "Email");
            code.ShouldNotBeNullOrWhiteSpace();

            await _accountProAppService.SetTwoFactorEnabledAsync(
                new SetTwoFactorEnabledInput { Enabled = false, Code = code });

            var refreshed = await _userManager.GetByIdAsync(user.Id);
            refreshed.TwoFactorEnabled.ShouldBeFalse();
        }
    }
}
