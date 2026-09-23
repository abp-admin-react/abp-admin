using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Volo.Abp.TenantManagement;
using Xunit;

namespace AbpAdmin.Account;

/* Linked Accounts 服务侧集成测试（六透镜审查轮补测——此前零覆盖）。
 * 覆盖：绑定成功链路、跨租户目标拒绝（host 上下文租户过滤器关闭，必须显式断言，
 * 否则 host 账号可绑定并切换进租户账号——提权）、自关联/重复绑定拒绝、
 * 错误密码计入目标账号锁定（防跨账号爆破）、未知/停用/密码错统一错误码（防枚举）、
 * 删除他人关联被拒、列表解析对端用户。
 * 切换取票的 grant 侧复查在 /connect/token 内（公开端点），由 LinkedAccountExtensionGrant
 * 实现；其直接单测依赖 OpenIddict 请求上下文构造成本高，关联一致性不变量由本文件钉住。
 * 测试环境 FakeCurrentPrincipalAccessor 固定当前用户为 host admin（Id: 2e701e62-…）。
 */
public abstract class AccountLinkTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IAccountLinkAppService _accountLinkAppService;
    private readonly IdentityUserManager _userManager;
    private readonly TenantManager _tenantManager;
    private readonly ITenantRepository _tenantRepository;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;
    private readonly ICurrentTenant _currentTenant;

    private static int _userSequence;

    protected AccountLinkTests()
    {
        _accountLinkAppService = GetRequiredService<IAccountLinkAppService>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _tenantManager = GetRequiredService<TenantManager>();
        _tenantRepository = GetRequiredService<ITenantRepository>();
        _currentPrincipalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
    }

    private async Task<IdentityUser> CreateTestUserAsync()
    {
        var seq = System.Threading.Interlocked.Increment(ref _userSequence);
        var user = new IdentityUser(Guid.NewGuid(), $"link-user-{seq}", $"link-user-{seq}@test.local");
        var result = await _userManager.CreateAsync(user, "Test@123456");
        result.Succeeded.ShouldBeTrue(string.Join("; ", result.Errors));
        return user;
    }

    /// <summary>测试默认主体是库中不存在的固定 host admin；服务调用必须包在真实用户的
    /// Change 作用域内，且 Change 作用域不能跨 await 边界创建（AsyncLocal 在异步方法返回时还原）。</summary>
    private IDisposable AsUser(IdentityUser user)
    {
        return ChangeCurrentUser(user.Id, user.UserName, user.Email!);
    }

    /// <summary>以指定用户身份执行操作（跨用户越权场景用）。</summary>
    private IDisposable ChangeCurrentUser(Guid userId, string userName, string email)
    {
        return _currentPrincipalAccessor.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, userId.ToString()),
            new Claim(AbpClaimTypes.UserName, userName),
            new Claim(AbpClaimTypes.Email, email)
        })));
    }

    [Fact]
    public async Task LinkAsync_Creates_Link_And_Returns_Peer()
    {
        var me = await CreateTestUserAsync();
        var peer = await CreateTestUserAsync();

        using (AsUser(me))
        {
            var dto = await _accountLinkAppService.LinkAsync(
                new LinkAccountInput { UserNameOrEmail = peer.UserName, Password = "Test@123456" });

        dto.UserId.ShouldBe(peer.Id);
        dto.UserName.ShouldBe(peer.UserName);
        dto.LinkId.ShouldNotBe(Guid.Empty);

        var list = await _accountLinkAppService.GetListAsync();
        list.ShouldContain(x => x.UserId == peer.Id && x.LinkId == dto.LinkId);
        }
    }

    [Fact]
    public async Task LinkAsync_Cross_Tenant_Target_Is_Rejected_With_Unified_Error()
    {
        // 关键安全不变量：host 上下文的租户过滤器是关闭的，FindByName 能解析到租户用户；
        // 服务必须显式拒绝跨租户绑定，否则 host 账号可经 linked-account grant 切换进
        // 租户账号（绕过模拟登录权限门禁与 impersonator 审计）
        var tenantName = $"link-t1-{Guid.NewGuid():N}".Substring(0, 20);
        var tenantAdminName = $"tadmin-{Guid.NewGuid():N}".Substring(0, 16);
        var tenant = await _tenantManager.CreateAsync(tenantName);
        await WithUnitOfWorkAsync(async () => { await _tenantRepository.InsertAsync(tenant); });

        using (_currentTenant.Change(tenant.Id))
        {
            await WithUnitOfWorkAsync(async () =>
            {
                // 用户名必须唯一：host 库种子了 admin 账号，重名会被 host 侧先解析到（同租户直通）
                var tenantUser = new IdentityUser(
                    Guid.NewGuid(), tenantAdminName,
                    $"tadmin-{Guid.NewGuid():N}@t.local", tenant.Id);
                (await _userManager.CreateAsync(tenantUser, "1q2w3E*")).Succeeded.ShouldBeTrue();
            });
        }

        var me = await CreateTestUserAsync();
        using (AsUser(me))
        {
            var exception = await Should.ThrowAsync<BusinessException>(
                () => _accountLinkAppService.LinkAsync(
                    new LinkAccountInput { UserNameOrEmail = tenantAdminName, Password = "1q2w3E*" }));

            exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.LinkedAccountInvalidCredentials);

            var list = await _accountLinkAppService.GetListAsync();
            list.ShouldBeEmpty("跨租户目标不得建立关联");
        }
    }

    [Fact]
    public async Task LinkAsync_Self_Is_Rejected()
    {
        var me = await CreateTestUserAsync();
        using (AsUser(me))
        {
            var exception = await Should.ThrowAsync<BusinessException>(
                () => _accountLinkAppService.LinkAsync(
                    new LinkAccountInput { UserNameOrEmail = me.UserName, Password = "Test@123456" }));

        exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.LinkedAccountSelfNotAllowed);
        }
    }

    [Fact]
    public async Task LinkAsync_Duplicate_Is_Rejected()
    {
        var me = await CreateTestUserAsync();
        var peer = await CreateTestUserAsync();

        using (AsUser(me))
        {
            await _accountLinkAppService.LinkAsync(
                new LinkAccountInput { UserNameOrEmail = peer.UserName, Password = "Test@123456" });

            var exception = await Should.ThrowAsync<BusinessException>(
                () => _accountLinkAppService.LinkAsync(
                    new LinkAccountInput { UserNameOrEmail = peer.UserName, Password = "Test@123456" }));

            exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.LinkedAccountAlreadyLinked);
        }
    }

    [Fact]
    public async Task LinkAsync_Wrong_Password_Counts_Toward_Target_Lockout()
    {
        var me = await CreateTestUserAsync();
        var peer = await CreateTestUserAsync();
        await _userManager.SetLockoutEnabledAsync(peer, true);

        using (AsUser(me))
        {
        // 默认 MaxFailedAccessAttempts = 5：错 5 次密码后目标账号必须被锁定
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var exception = await Should.ThrowAsync<BusinessException>(
                () => _accountLinkAppService.LinkAsync(
                    new LinkAccountInput { UserNameOrEmail = peer.UserName, Password = "Wrong@123" }));
            exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.LinkedAccountInvalidCredentials);
        }
        }

        var refreshed = await _userManager.GetByIdAsync(peer.Id);
        (await _userManager.IsLockedOutAsync(refreshed)).ShouldBeTrue("错误密码必须计入目标账号锁定（防跨账号爆破）");
    }

    [Fact]
    public async Task LinkAsync_Unknown_And_Disabled_Targets_Share_Error_Code()
    {
        // 防枚举：未知账号与停用账号（以及密码错误）必须返回同一错误码
        var me = await CreateTestUserAsync();
        var disabled = await CreateTestUserAsync();
        disabled.SetIsActive(false);
        (await _userManager.UpdateAsync(disabled)).Succeeded.ShouldBeTrue();

        using (AsUser(me))
        {
        var unknownException = await Should.ThrowAsync<BusinessException>(
            () => _accountLinkAppService.LinkAsync(
                new LinkAccountInput { UserNameOrEmail = $"ghost-{Guid.NewGuid():N}@test.local", Password = "Whatever@1" }));

        var disabledException = await Should.ThrowAsync<BusinessException>(
            () => _accountLinkAppService.LinkAsync(
                new LinkAccountInput { UserNameOrEmail = disabled.UserName, Password = "Whatever@1" }));

        unknownException.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.LinkedAccountInvalidCredentials);
        disabledException.Code.ShouldBe(unknownException.Code, "未知与停用不得区分（防枚举）");
        }
    }

    [Fact]
    public async Task DeleteAsync_Link_Of_Other_User_Throws_NotFound()
    {
        var me = await CreateTestUserAsync();
        var peer = await CreateTestUserAsync();

        using (AsUser(me))
        {
            var dto = await _accountLinkAppService.LinkAsync(
                new LinkAccountInput { UserNameOrEmail = peer.UserName, Password = "Test@123456" });

            // 第三个用户尝试删除 me↔peer 的关联：必须 LinkedAccountNotFound（防越权）
            var stranger = await CreateTestUserAsync();
            using (ChangeCurrentUser(stranger.Id, stranger.UserName, stranger.Email))
            {
                var exception = await Should.ThrowAsync<BusinessException>(
                    () => _accountLinkAppService.DeleteAsync(dto.LinkId));
                exception.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.LinkedAccountNotFound);
            }

            // 关联仍在：me 视角仍可见
            var list = await _accountLinkAppService.GetListAsync();
            list.ShouldContain(x => x.LinkId == dto.LinkId);
        }
    }
}
