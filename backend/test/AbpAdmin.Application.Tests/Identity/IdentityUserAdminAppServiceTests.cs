using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using Microsoft.Extensions.Localization;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace AbpAdmin.Identity;

/* T2.6 Identity Pro 缺口集成测试。
 * 覆盖场景：
 * 1. 密码有效期：GetCurrentAccountStatusAsync 返回正确状态
 * 2. 强制改密：RequireChangePasswordOnNextLoginAsync 设置标志
 *
 * 导入导出与周期改密用例已按规格要求归拢到同目录 UserImportExportTests.cs。
 *
 * 测试环境 FakeCurrentPrincipalAccessor 固定当前用户为 admin
 * (Id: 2e701e62-0953-4dd3-910b-dc6cc93ccb0d)。
 */
public abstract class IdentityUserAdminAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly IIdentityUserAdminAppService _identityUserAdminAppService;
    private readonly IdentityUserManager _userManager;
    private readonly IIdentityUserRepository _userRepository;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly IStringLocalizer<AbpAdminResource> _localizer;

    protected IdentityUserAdminAppServiceTests()
    {
        _identityUserAdminAppService = GetRequiredService<IIdentityUserAdminAppService>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _userRepository = GetRequiredService<IIdentityUserRepository>();
        _currentPrincipalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        // AccountStatus.Reason 已本地化（重构报告问题 9），断言走同一本地化管线
        _localizer = GetRequiredService<IStringLocalizer<AbpAdminResource>>();
    }

    /// <summary>
    /// 以指定用户身份执行操作。用于跨用户场景测试。
    /// </summary>
    private IDisposable ChangeCurrentUser(Guid userId, string userName, string email)
    {
        return _currentPrincipalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, userName),
            new Claim(AbpClaimTypes.Email, email)
        })));
    }

    /// <summary>
    /// 创建测试用户并返回用户 ID。
    /// </summary>
    private async Task<Guid> CreateTestUserAsync(string userName, string email, string password)
    {
        var user = new IdentityUser(
            Guid.NewGuid(),
            userName,
            email);

        var result = await _userManager.CreateAsync(user, password);
        result.Succeeded.ShouldBeTrue(
            string.Join("; ", result.Errors.Select(e => e.Description)));

        return user.Id;
    }

    /// <summary>
    /// 清理测试用户。
    /// </summary>
    private async Task CleanupTestUserAsync(Guid userId)
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _userManager.FindByIdAsync(userId.ToString());
            if (user != null)
            {
                await _userManager.DeleteAsync(user);
            }
        });
    }

    #region 密码有效期测试

    [Fact]
    public async Task GetCurrentAccountStatusAsync_Should_Return_False_For_Normal_User()
    {
        // Arrange - 创建一个新用户（密码未过期）
        var testUserId = await CreateTestUserAsync("status-test-normal", "status-normal@test.com", "Test@123456");

        using (ChangeCurrentUser(testUserId, "status-test-normal", "status-normal@test.com"))
        {
            // Act
            var result = await _identityUserAdminAppService.GetCurrentAccountStatusAsync();

            // Assert
            result.ShouldNotBeNull();
            result.ShouldChangePassword.ShouldBeFalse();
            result.Reason.ShouldBeNull();
        }

        // Cleanup
        await CleanupTestUserAsync(testUserId);
    }

    [Fact]
    public async Task GetCurrentAccountStatusAsync_Should_Return_True_When_Admin_Requires_Change()
    {
        // Arrange - 创建一个新用户
        var testUserId = await CreateTestUserAsync("status-test-force", "status-force@test.com", "Test@123456");

        // 管理员要求改密
        await _identityUserAdminAppService.RequireChangePasswordOnNextLoginAsync(testUserId);

        using (ChangeCurrentUser(testUserId, "status-test-force", "status-force@test.com"))
        {
            // Act
            var result = await _identityUserAdminAppService.GetCurrentAccountStatusAsync();

            // Assert
            result.ShouldNotBeNull();
            result.ShouldChangePassword.ShouldBeTrue();
            result.Reason.ShouldNotBeNullOrEmpty();
            result.Reason.ShouldContain(_localizer["AccountStatus:AdminForceChangePassword"].Value);
        }

        // Cleanup
        await CleanupTestUserAsync(testUserId);
    }

    [Fact]
    public async Task GetCurrentAccountStatusAsync_Should_Return_False_For_External_User()
    {
        // Arrange - 创建一个外部用户
        var user = new IdentityUser(
            Guid.NewGuid(),
            "status-test-external",
            "status-external@test.com")
        {
            IsExternal = true
        };

        var createResult = await _userManager.CreateAsync(user);
        createResult.Succeeded.ShouldBeTrue();

        using (ChangeCurrentUser(user.Id, "status-test-external", "status-external@test.com"))
        {
            // Act
            var result = await _identityUserAdminAppService.GetCurrentAccountStatusAsync();

            // Assert
            result.ShouldNotBeNull();
            result.ShouldChangePassword.ShouldBeFalse();
        }

        // Cleanup
        await CleanupTestUserAsync(user.Id);
    }

    #endregion

    #region 强制改密测试

    [Fact]
    public async Task RequireChangePasswordOnNextLoginAsync_Should_Set_Flag()
    {
        // Arrange - 创建一个新用户
        var testUserId = await CreateTestUserAsync("force-change-test", "force-change@test.com", "Test@123456");

        // 验证初始状态
        var userBefore = await _userManager.GetByIdAsync(testUserId);
        userBefore.ShouldChangePasswordOnNextLogin.ShouldBeFalse();

        // Act
        await _identityUserAdminAppService.RequireChangePasswordOnNextLoginAsync(testUserId);

        // Assert
        var userAfter = await _userManager.GetByIdAsync(testUserId);
        userAfter.ShouldChangePasswordOnNextLogin.ShouldBeTrue();

        // Cleanup
        await CleanupTestUserAsync(testUserId);
    }

    #endregion

    #region 双因素认证测试（管理端按用户 2FA 开关，对标 ABP Identity Pro）

    [Fact]
    public async Task SetTwoFactorEnabledAsync_Should_Throw_Without_Confirmed_Provider()
    {
        // Arrange - 新用户邮箱/手机均未确认：启用 2FA 后登录流无第二因子可投递，必须拒绝
        var testUserId = await CreateTestUserAsync("2fa-no-provider", "2fa-no-provider@test.com", "Test@123456");

        try
        {
            // Act & Assert
            var exception = await Should.ThrowAsync<BusinessException>(async () =>
                await _identityUserAdminAppService.SetTwoFactorEnabledAsync(
                    testUserId, new SetUserTwoFactorEnabledDto { Enabled = true }));
            exception.Code.ShouldBe("AbpAdmin:TwoFactorRequiresConfirmedProvider");
        }
        finally
        {
            await CleanupTestUserAsync(testUserId);
        }
    }

    [Fact]
    public async Task SetTwoFactorEnabledAsync_Should_Enable_After_Email_Confirmed_And_Disable()
    {
        var testUserId = await CreateTestUserAsync("2fa-admin-toggle", "2fa-admin-toggle@test.com", "Test@123456");

        try
        {
            // Arrange - 走正规流程确认邮箱（不直接改实体，保持与运行时一致）
            var user = await _userManager.GetByIdAsync(testUserId);
            var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
            (await _userManager.ConfirmEmailAsync(user, token)).Succeeded.ShouldBeTrue();
            var stampBefore = (await _userManager.GetByIdAsync(testUserId)).SecurityStamp;

            // Act - 管理端启用
            await _identityUserAdminAppService.SetTwoFactorEnabledAsync(
                testUserId, new SetUserTwoFactorEnabledDto { Enabled = true });

            var afterEnable = await _userManager.GetByIdAsync(testUserId);
            afterEnable.TwoFactorEnabled.ShouldBeTrue();
            // 启用必须经 UserManager（内部轮换 security stamp 使该用户现存令牌失效），
            // 绕过 UserManager 直改实体的写法在这里会红
            afterEnable.SecurityStamp.ShouldNotBe(stampBefore);

            // Act - 管理端禁用
            await _identityUserAdminAppService.SetTwoFactorEnabledAsync(
                testUserId, new SetUserTwoFactorEnabledDto { Enabled = false });

            (await _userManager.GetByIdAsync(testUserId)).TwoFactorEnabled.ShouldBeFalse();
        }
        finally
        {
            await CleanupTestUserAsync(testUserId);
        }
    }

    [Fact]
    public async Task SetTwoFactorEnabledAsync_Disable_Should_Not_Require_Confirmed_Provider()
    {
        // 禁用是安全方向的操作，不应被「无已确认联系方式」拦下
        var testUserId = await CreateTestUserAsync("2fa-disable", "2fa-disable@test.com", "Test@123456");

        try
        {
            await _identityUserAdminAppService.SetTwoFactorEnabledAsync(
                testUserId, new SetUserTwoFactorEnabledDto { Enabled = false });

            (await _userManager.GetByIdAsync(testUserId)).TwoFactorEnabled.ShouldBeFalse();
        }
        finally
        {
            await CleanupTestUserAsync(testUserId);
        }
    }

    #endregion
}
