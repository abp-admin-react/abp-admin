using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Account;

/* T2.7 模拟登录集成测试。
 * 覆盖：模拟签发含 impersonator claim（ImpersonationManager 产出的 claim 即 grant 写入令牌的内容）、
 * 租户模拟解析到目标租户的 admin 用户、BackToMyAccount 正常返回、
 * 无 impersonator claim 的普通身份调返回端点被拒（AbpAuthorizationException → 403）。
 * 测试环境 FakeCurrentPrincipalAccessor 固定当前用户为 host admin
 * (Id: 2e701e62-0953-4dd3-910b-dc6cc93ccb0d)。
 */
public abstract class ImpersonationTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private readonly ImpersonationManager _impersonationManager;
    private readonly IAccountProAppService _accountProAppService;
    private readonly IdentityUserManager _userManager;
    private readonly IdentityRoleManager _roleManager;
    private readonly TenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;

    private static int _userSequence;

    protected ImpersonationTests()
    {
        _impersonationManager = GetRequiredService<ImpersonationManager>();
        _accountProAppService = GetRequiredService<IAccountProAppService>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _roleManager = GetRequiredService<IdentityRoleManager>();
        _tenantManager = GetRequiredService<TenantManager>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _currentPrincipalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    private async Task<IdentityUser> CreateTestUserAsync()
    {
        var seq = System.Threading.Interlocked.Increment(ref _userSequence);
        var user = new IdentityUser(Guid.NewGuid(), $"imp-user-{seq}", $"imp-user-{seq}@test.local");
        var result = await _userManager.CreateAsync(user, "Test@123456");
        result.Succeeded.ShouldBeTrue(string.Join("; ", result.Errors));
        return user;
    }

    private async Task<Guid> CreateTenantWithAdminAsync(string name, string adminEmail)
    {
        var tenant = await _tenantManager.CreateAsync(name);
        await WithUnitOfWorkAsync(async () =>
        {
            await _tenantRepository.InsertAsync(tenant);
        });

        // 直接创建租户 admin 角色与管理员用户（不走完整 DataSeeder，
        // 避免权限授予种子与既有静态权限存储冲突）。
        // CurrentTenant.Change 必须在 UoW 外层（00-overview 6.5）。
        using (_currentTenant.Change(tenant.Id))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                var adminRole = new IdentityRole(Guid.NewGuid(), "admin", tenant.Id)
                {
                    IsStatic = true,
                    IsPublic = true
                };
                (await _roleManager.CreateAsync(adminRole)).Succeeded.ShouldBeTrue();

                var adminUser = new IdentityUser(Guid.NewGuid(), "admin", adminEmail, tenant.Id);
                (await _userManager.CreateAsync(adminUser, "1q2w3E*")).Succeeded.ShouldBeTrue();
                (await _userManager.AddToRoleAsync(adminUser, "admin")).Succeeded.ShouldBeTrue();
            });
        }

        return tenant.Id;
    }

    [Fact]
    public async Task User_impersonation_target_carries_impersonator_claims()
    {
        var targetUser = await CreateTestUserAsync();

        var target = await _impersonationManager.CreateUserImpersonationAsync(targetUser.Id);

        target.User.Id.ShouldBe(targetUser.Id);
        // 这些 claim 会被 grant 写入签发的令牌，审计日志的 ImpersonatorUserId 列即从此取值
        target.ImpersonatorClaims.ShouldContain(c =>
            c.Type == AbpClaimTypes.ImpersonatorUserId && c.Value == AdminUserId.ToString());
        // host 管理员没有租户上下文，不应写 impersonator_tenant_id
        target.ImpersonatorClaims.ShouldNotContain(c => c.Type == AbpClaimTypes.ImpersonatorTenantId);
    }

    [Fact]
    public async Task User_impersonation_rejects_unknown_target()
    {
        var exception = await Assert.ThrowsAsync<Volo.Abp.BusinessException>(
            () => _impersonationManager.CreateUserImpersonationAsync(Guid.NewGuid()));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.ImpersonationTargetUserNotFound);
    }

    [Fact]
    public async Task Tenant_impersonation_resolves_tenant_admin_and_carries_claims()
    {
        var tenantId = await CreateTenantWithAdminAsync($"imp-tenant-{Guid.NewGuid():N}".Substring(0, 20), "admin@imp-tenant.test");

        // 上面的 WithUnitOfWorkAsync 释放了其 scope 创建的共享 DbContext（00-overview 6.1：
        // DbContext 挂在 UoW 上而非 DI scope），这里用独立 UoW 拿到全新的上下文再调 manager。
        ImpersonationTarget? target = null;
        using (var scope = ServiceProvider.CreateScope())
        {
            var uowManager = scope.ServiceProvider.GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>();
            using (var uow = uowManager.Begin(new Volo.Abp.Uow.AbpUnitOfWorkOptions(), requiresNew: true))
            {
                target = await _impersonationManager.CreateTenantImpersonationAsync(tenantId);
                await uow.CompleteAsync();
            }
        }

        target.ShouldNotBeNull();
        target!.User.Email.ShouldBe("admin@imp-tenant.test");
        target.User.TenantId.ShouldBe(tenantId);
        target.ImpersonatorClaims.ShouldContain(c =>
            c.Type == AbpClaimTypes.ImpersonatorUserId && c.Value == AdminUserId.ToString());
    }

    [Fact]
    public async Task Tenant_impersonation_rejects_unknown_tenant()
    {
        var exception = await Assert.ThrowsAsync<Volo.Abp.BusinessException>(
            () => _impersonationManager.CreateTenantImpersonationAsync(Guid.NewGuid()));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.ImpersonationTenantNotFound);
    }

    [Fact]
    public async Task Back_to_my_account_resolves_original_user()
    {
        // 测试库里真实存在的 admin 用户（种子每次生成随机 Id，不能写死）
        var adminUser = await _userManager.FindByNameAsync("admin");
        adminUser.ShouldNotBeNull();

        // 构造「模拟会话中的当前身份」：目标用户身份 + impersonator claim 指向原管理员
        var impersonatedPrincipal = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, Guid.NewGuid().ToString()),
            new Claim(AbpClaimTypes.UserName, "impersonated-user"),
            new Claim(AbpClaimTypes.ImpersonatorUserId, adminUser!.Id.ToString())
        }));

        using (_currentPrincipalAccessor.Change(impersonatedPrincipal))
        {
            var originalUser = await _impersonationManager.GetImpersonatorUserOrThrowAsync();

            originalUser.Id.ShouldBe(adminUser.Id);
            originalUser.UserName.ShouldBe("admin");
        }
    }

    [Fact]
    public async Task Back_to_my_account_without_impersonator_claims_is_rejected()
    {
        // 默认 FakeCurrentPrincipalAccessor 是不带 impersonator claim 的普通管理员身份
        await Assert.ThrowsAsync<AbpAuthorizationException>(
            () => _impersonationManager.GetImpersonatorUserOrThrowAsync());

        // 应用服务入口同样必须先被拒（403 语义在 HTTP 交换之前生效，不依赖 Web 环境）
        await Assert.ThrowsAsync<AbpAuthorizationException>(
            () => _accountProAppService.BackToMyAccountAsync());
    }
}
