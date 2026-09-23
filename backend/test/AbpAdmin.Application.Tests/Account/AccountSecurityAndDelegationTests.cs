using System;
using System.Threading.Tasks;
using AbpAdmin.Account;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.Account;

public abstract class AccountSecurityAndDelegationTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private static readonly Guid AdminUserId = Guid.Parse("2e701e62-0953-4dd3-910b-dc6cc93ccb0d");

    private async Task EnsureHostAdminExistsAsync()
    {
        var userManager = GetRequiredService<IdentityUserManager>();
        if (await userManager.FindByIdAsync(AdminUserId.ToString()) is not null)
        {
            return;
        }

        var user = new IdentityUser(AdminUserId, "host-admin-security", "host-admin-security@test.local");
        (await userManager.CreateAsync(user, "1q2w3E*")).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task Should_List_External_Logins()
    {
        await EnsureHostAdminExistsAsync();
        var service = GetRequiredService<IAccountSecurityAppService>();
        var logins = await service.GetLoginsAsync();
        logins.ShouldNotBeNull();
    }

    [Fact]
    public async Task Should_Generate_Authenticator_Key()
    {
        await EnsureHostAdminExistsAsync();
        var service = GetRequiredService<IAccountSecurityAppService>();
        var key = await service.ResetAuthenticatorKeyAsync();
        key.SharedKey.ShouldNotBeNullOrWhiteSpace();
        key.AuthenticatorUri.ShouldContain("otpauth://totp/");
    }

    [Fact]
    public async Task Should_Reject_Invalid_Authenticator_Code()
    {
        await EnsureHostAdminExistsAsync();
        var service = GetRequiredService<IAccountSecurityAppService>();
        await service.ResetAuthenticatorKeyAsync();
        var ex = await Should.ThrowAsync<BusinessException>(() =>
            service.EnableAuthenticatorAsync(new EnableAuthenticatorInput { Code = "000000" }));
        ex.Code.ShouldBe(AbpAdminDomainErrorCodes.Account.InvalidAuthenticatorCode);
    }

    [Fact]
    public async Task Should_Delegate_And_Delete()
    {
        await EnsureHostAdminExistsAsync();
        var userManager = GetRequiredService<IdentityUserManager>();
        var delegation = GetRequiredService<IIdentityUserDelegationAppService>();

        var target = new IdentityUser(Guid.NewGuid(), "delegatee", "delegatee@abp.io");
        (await userManager.CreateAsync(target, "1q2w3E*")).Succeeded.ShouldBeTrue();

        var created = await delegation.DelegateAsync(new CreateUserDelegationInput
        {
            TargetUserId = target.Id,
            StartTime = DateTime.UtcNow.AddMinutes(-1),
            EndTime = DateTime.UtcNow.AddDays(1)
        });

        created.SourceUserId.ShouldBe(AdminUserId);
        created.TargetUserId.ShouldBe(target.Id);

        var mine = await delegation.GetDelegatedToOthersAsync();
        mine.ShouldContain(x => x.Id == created.Id);

        await delegation.DeleteAsync(created.Id);
        (await delegation.GetDelegatedToOthersAsync()).ShouldNotContain(x => x.Id == created.Id);
    }

    [Fact]
    public async Task Should_Not_Delegate_To_Self()
    {
        var delegation = GetRequiredService<IIdentityUserDelegationAppService>();
        await Should.ThrowAsync<BusinessException>(() =>
            delegation.DelegateAsync(new CreateUserDelegationInput
            {
                TargetUserId = AdminUserId,
                StartTime = DateTime.UtcNow,
                EndTime = DateTime.UtcNow.AddDays(1)
            }));
    }
}
