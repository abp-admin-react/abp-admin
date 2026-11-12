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

/* 密码有效期与强制改密。直接角色见本类后半；导出与周期改密在 UserImportExportTests。
 * FakeCurrentPrincipalAccessor 固定当前用户为 admin
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
            // 整段包进一个 UoW：ABP 10.7.0 把 Identity token providers 移到域层后，
            // AbpSingleActiveTokenProvider.GenerateAsync 会经仓储加载用户令牌集合，需要环境 UoW
            // （生产路径无此问题：应用服务与 Razor 页处理器都在 ABP 的 UoW 过滤器内）。
            // 必须整段一个 UoW 而不是只包 Generate：内外两个 UoW 各自加载同一用户会造成
            // EF 双实例跟踪冲突（"another instance ... is already being tracked"）。
            // wrap 内断言读的是跟踪实例（内存态），wrap 外另做 fresh read 验证提交后的
            // 持久化状态——两层各自把关，缺一层都会留下空洞。
            string? stampBefore = null;
            await WithUnitOfWorkAsync(async () =>
            {
                // Arrange - 走正规流程确认邮箱（不直接改实体，保持与运行时一致）
                var user = await _userManager.GetByIdAsync(testUserId);
                var token = await _userManager.GenerateEmailConfirmationTokenAsync(user);
                (await _userManager.ConfirmEmailAsync(user, token)).Succeeded.ShouldBeTrue();
                stampBefore = (await _userManager.GetByIdAsync(testUserId)).SecurityStamp;

                // Act - 管理端启用
                await _identityUserAdminAppService.SetTwoFactorEnabledAsync(
                    testUserId, new SetUserTwoFactorEnabledDto { Enabled = true });

                var afterEnable = await _userManager.GetByIdAsync(testUserId);
                afterEnable.TwoFactorEnabled.ShouldBeTrue();
                // 启用必须经 UserManager（内部轮换 security stamp 使该用户现存令牌失效），
                // 绕过 UserManager 直改实体的写法在这里会红（此处读的是跟踪实例，
                // 持久化验证在 wrap 外）
                afterEnable.SecurityStamp.ShouldNotBe(stampBefore);

                // Act - 管理端禁用
                await _identityUserAdminAppService.SetTwoFactorEnabledAsync(
                    testUserId, new SetUserTwoFactorEnabledDto { Enabled = false });

                (await _userManager.GetByIdAsync(testUserId)).TwoFactorEnabled.ShouldBeFalse();
            });

            // wrap 外 fresh read（新的 UoW/DbContext）：验证单 UoW 提交后的持久化状态——
            // stamp 已轮换且最终处于禁用态，两级断言补齐"内存态 + 持久化态"。
            var persisted = await _userManager.GetByIdAsync(testUserId);
            persisted.TwoFactorEnabled.ShouldBeFalse();
            persisted.SecurityStamp.ShouldNotBe(stampBefore);
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

    [Fact]
    public async Task GetDirectRoleNamesAsync_Should_Throw_When_User_Not_Found()
    {
        await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() =>
            _identityUserAdminAppService.GetDirectRoleNamesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetDirectRoleNamesAsync_Should_Return_Empty_When_User_Has_Only_Ou_Derived_Roles()
    {
        var roleManager = GetRequiredService<IdentityRoleManager>();
        var ouRepository = GetRequiredService<IOrganizationUnitRepository>();
        var ouManager = GetRequiredService<OrganizationUnitManager>();
        var ouRoleName = "ou-only-" + Guid.NewGuid().ToString("N")[..8];
        var testUserId = await CreateTestUserAsync(
            "ou-only-" + Guid.NewGuid().ToString("N")[..6],
            "ou-only@test.com",
            "Test@123456");

        Guid? ouId = null;
        Guid? ouRoleId = null;
        try
        {
            // CreateAsync 与 AddRole 分 UoW：同事务内 AddRole 会再 Get OU，未提交则 EntityNotFound。
            await WithUnitOfWorkAsync(async () =>
            {
                var ouRole = new IdentityRole(Guid.NewGuid(), ouRoleName);
                (await roleManager.CreateAsync(ouRole)).Succeeded.ShouldBeTrue();
                ouRoleId = ouRole.Id;

                var ou = new OrganizationUnit(Guid.NewGuid(), "ou-only-dept-" + Guid.NewGuid().ToString("N")[..6]);
                await ouManager.CreateAsync(ou);
                ouId = ou.Id;
            });
            await WithUnitOfWorkAsync(async () =>
            {
                await ouManager.AddRoleToOrganizationUnitAsync(ouRoleId!.Value, ouId!.Value);
                var user = await _userManager.GetByIdAsync(testUserId);
                var ou = await ouRepository.GetAsync(ouId.Value);
                await _userManager.AddToOrganizationUnitAsync(user, ou);
            });

            var effective = await WithUnitOfWorkAsync(async () =>
                await _userManager.GetRolesAsync(await _userManager.GetByIdAsync(testUserId)));
            effective.ShouldContain(ouRoleName);

            var direct = await _identityUserAdminAppService.GetDirectRoleNamesAsync(testUserId);
            direct.Items.ShouldBeEmpty();
        }
        finally
        {
            await CleanupTestUserAsync(testUserId);
            // 先删角色再删 OU：RoleManager.Delete 会扫 OU 成员缓存，OU 已删会 EntityNotFound。
            await WithUnitOfWorkAsync(async () =>
            {
                if (ouRoleId is Guid or)
                {
                    var role = await roleManager.FindByIdAsync(or.ToString());
                    if (role != null)
                    {
                        await roleManager.DeleteAsync(role);
                    }
                }
            });
            await WithUnitOfWorkAsync(async () =>
            {
                if (ouId is Guid id)
                {
                    var ou = await ouRepository.FindAsync(id);
                    if (ou != null)
                    {
                        await ouManager.DeleteAsync(ou.Id);
                    }
                }
            });
        }
    }

    [Fact]
    public async Task GetDirectRoleNamesAsync_Should_Exclude_Organization_Unit_Derived_Roles()
    {
        var roleManager = GetRequiredService<IdentityRoleManager>();
        var ouRepository = GetRequiredService<IOrganizationUnitRepository>();
        var ouManager = GetRequiredService<OrganizationUnitManager>();
        var directRoleName = "direct-only-" + Guid.NewGuid().ToString("N")[..8];
        var ouRoleName = "ou-derived-" + Guid.NewGuid().ToString("N")[..8];
        var testUserId = await CreateTestUserAsync(
            "direct-roles-" + Guid.NewGuid().ToString("N")[..6],
            "direct-roles@test.com",
            "Test@123456");

        Guid? ouId = null;
        Guid? directRoleId = null;
        Guid? ouRoleId = null;
        try
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var directRole = new IdentityRole(Guid.NewGuid(), directRoleName);
                var ouRole = new IdentityRole(Guid.NewGuid(), ouRoleName);
                (await roleManager.CreateAsync(directRole)).Succeeded.ShouldBeTrue();
                (await roleManager.CreateAsync(ouRole)).Succeeded.ShouldBeTrue();
                directRoleId = directRole.Id;
                ouRoleId = ouRole.Id;

                var ou = new OrganizationUnit(Guid.NewGuid(), "direct-roles-ou-" + Guid.NewGuid().ToString("N")[..6]);
                await ouManager.CreateAsync(ou);
                ouId = ou.Id;
            });
            await WithUnitOfWorkAsync(async () =>
            {
                await ouManager.AddRoleToOrganizationUnitAsync(ouRoleId!.Value, ouId!.Value);
                var user = await _userManager.GetByIdAsync(testUserId);
                (await _userManager.AddToRoleAsync(user, directRoleName)).Succeeded.ShouldBeTrue();
                var ou = await ouRepository.GetAsync(ouId.Value);
                await _userManager.AddToOrganizationUnitAsync(user, ou);
            });

            // UserManager.GetRolesAsync 含 OU 并集；管理端编辑表单必须只见直接角色。
            var effective = await WithUnitOfWorkAsync(async () =>
                await _userManager.GetRolesAsync(await _userManager.GetByIdAsync(testUserId)));
            effective.ShouldContain(directRoleName);
            effective.ShouldContain(ouRoleName);

            var direct = await _identityUserAdminAppService.GetDirectRoleNamesAsync(testUserId);
            direct.Items.ShouldBe(new[] { directRoleName }, ignoreOrder: true);
        }
        finally
        {
            await CleanupTestUserAsync(testUserId);
            await WithUnitOfWorkAsync(async () =>
            {
                if (directRoleId is Guid dr)
                {
                    var role = await roleManager.FindByIdAsync(dr.ToString());
                    if (role != null)
                    {
                        await roleManager.DeleteAsync(role);
                    }
                }

                if (ouRoleId is Guid or)
                {
                    var role = await roleManager.FindByIdAsync(or.ToString());
                    if (role != null)
                    {
                        await roleManager.DeleteAsync(role);
                    }
                }
            });
            await WithUnitOfWorkAsync(async () =>
            {
                if (ouId is Guid id)
                {
                    var ou = await ouRepository.FindAsync(id);
                    if (ou != null)
                    {
                        await ouManager.DeleteAsync(ou.Id);
                    }
                }
            });
        }
    }

    [Fact]
    public async Task UpdateRoles_Should_Not_Bake_Organization_Unit_Roles_Into_Direct_Membership()
    {
        var users = GetRequiredService<IIdentityUserAppService>();
        var lookup = GetRequiredService<IUserDirectRoleNameLookup>();
        var roleManager = GetRequiredService<IdentityRoleManager>();
        var ouRepository = GetRequiredService<IOrganizationUnitRepository>();
        var ouManager = GetRequiredService<OrganizationUnitManager>();
        var directRoleName = "keep-direct-" + Guid.NewGuid().ToString("N")[..8];
        var ouRoleName = "echo-ou-" + Guid.NewGuid().ToString("N")[..8];
        var testUserId = await CreateTestUserAsync(
            "echo-roles-" + Guid.NewGuid().ToString("N")[..6],
            "echo-roles@test.com",
            "Test@123456");

        Guid? ouId = null;
        Guid? directRoleId = null;
        Guid? ouRoleId = null;
        try
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var directRole = new IdentityRole(Guid.NewGuid(), directRoleName);
                var ouRole = new IdentityRole(Guid.NewGuid(), ouRoleName);
                (await roleManager.CreateAsync(directRole)).Succeeded.ShouldBeTrue();
                (await roleManager.CreateAsync(ouRole)).Succeeded.ShouldBeTrue();
                directRoleId = directRole.Id;
                ouRoleId = ouRole.Id;
                var ou = new OrganizationUnit(Guid.NewGuid(), "echo-ou-" + Guid.NewGuid().ToString("N")[..6]);
                await ouManager.CreateAsync(ou);
                ouId = ou.Id;
            });
            await WithUnitOfWorkAsync(async () =>
            {
                await ouManager.AddRoleToOrganizationUnitAsync(ouRoleId!.Value, ouId!.Value);
                var user = await _userManager.GetByIdAsync(testUserId);
                (await _userManager.AddToRoleAsync(user, directRoleName)).Succeeded.ShouldBeTrue();
                await _userManager.AddToOrganizationUnitAsync(user, await ouRepository.GetAsync(ouId.Value));
            });

            // 默认测试主体是 admin。回写有效并集（含部门角色）不得写入 AbpUserRoles。
            await users.UpdateRolesAsync(testUserId, new IdentityUserUpdateRolesDto
            {
                RoleNames = [directRoleName, ouRoleName],
            });

            var direct = await lookup.GetAsync(testUserId);
            direct.ShouldBe(new[] { directRoleName }, ignoreOrder: true);
            var effective = await WithUnitOfWorkAsync(async () =>
                await _userManager.GetRolesAsync(await _userManager.GetByIdAsync(testUserId)));
            effective.ShouldContain(ouRoleName);
        }
        finally
        {
            await CleanupTestUserAsync(testUserId);
            await WithUnitOfWorkAsync(async () =>
            {
                foreach (var id in new[] { directRoleId, ouRoleId })
                {
                    if (id is not Guid roleId)
                    {
                        continue;
                    }

                    var role = await roleManager.FindByIdAsync(roleId.ToString());
                    if (role != null)
                    {
                        await roleManager.DeleteAsync(role);
                    }
                }
            });
            await WithUnitOfWorkAsync(async () =>
            {
                if (ouId is Guid id)
                {
                    var ou = await ouRepository.FindAsync(id);
                    if (ou != null)
                    {
                        await ouManager.DeleteAsync(ou.Id);
                    }
                }
            });
        }
    }
}
