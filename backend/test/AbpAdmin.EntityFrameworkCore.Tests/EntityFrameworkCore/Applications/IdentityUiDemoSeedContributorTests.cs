using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Identity;
using Microsoft.Extensions.Configuration;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class IdentityUiDemoSeedContributorTests
    : AbpAdminApplicationTestBase<AbpAdminEntityFrameworkCoreTestModule>
{
    private const string EnabledKey = "App:SeedIdentityUiDemo";
    private const string PasswordKey = "App:IdentityUiDemoPassword";
    private const string ReaderRole = "ou-demo-reader";
    private const string ProjectCode = "demo.project_code";

    [Fact]
    public async Task Should_Leave_Demo_Disabled_By_Default_And_Skip_Tenant_Seeds()
    {
        var configuration = GetRequiredService<IConfiguration>();
        configuration[EnabledKey] = null;
        var contributor = GetRequiredService<IdentityUiDemoSeedContributor>();
        var claimTypes = GetRequiredService<IIdentityClaimTypeRepository>();

        await contributor.SeedAsync(new DataSeedContext());
        (await claimTypes.AnyAsync(ProjectCode)).ShouldBeFalse();

        configuration[EnabledKey] = "true";
        await contributor.SeedAsync(new DataSeedContext(Guid.NewGuid()));
        (await claimTypes.AnyAsync(ProjectCode)).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Require_A_Configured_Password_For_New_Demo_Accounts()
    {
        var configuration = GetRequiredService<IConfiguration>();
        configuration[EnabledKey] = "true";
        configuration[PasswordKey] = null;
        var contributor = GetRequiredService<IdentityUiDemoSeedContributor>();

        await Should.ThrowAsync<InvalidOperationException>(() => contributor.SeedAsync(new DataSeedContext()));
        var users = GetRequiredService<IIdentityUserRepository>();
        (await users.FindByNormalizedUserNameAsync("OU-DEMO-DEV")).ShouldBeNull();
    }

    [Fact]
    public async Task Should_Create_Demo_Once_And_Preserve_An_Existing_Claim_Value()
    {
        var configuration = GetRequiredService<IConfiguration>();
        configuration[EnabledKey] = "true";
        configuration[PasswordKey] = $"Aa!9{Guid.NewGuid():N}";

        var contributor = GetRequiredService<IdentityUiDemoSeedContributor>();
        await contributor.SeedAsync(new DataSeedContext());

        var users = GetRequiredService<IIdentityUserRepository>();
        var manager = GetRequiredService<IdentityUserManager>();
        await WithUnitOfWorkAsync(async () =>
        {
            var developer = await users.FindByNormalizedUserNameAsync("OU-DEMO-DEV");
            var original = new Claim(ProjectCode, "DEMO-ALPHA");
            var replacement = new Claim(ProjectCode, "DEMO-BETA");
            (await manager.ReplaceClaimAsync(developer, original, replacement)).Succeeded.ShouldBeTrue();
        });

        configuration[PasswordKey] = null;
        await contributor.SeedAsync(new DataSeedContext());

        var claimTypes = GetRequiredService<IIdentityClaimTypeRepository>();
        var claimNames = (await claimTypes.GetListAsync()).Where(type => type.Name.StartsWith("demo."))
            .Select(type => type.Name).OrderBy(name => name).ToArray();
        claimNames.ShouldBe(new[] { "demo.badge_label", ProjectCode, "demo.support_region" });

        var departments = GetRequiredService<IOrganizationUnitRepository>();
        var demoDepartments = (await departments.GetListAsync())
            .Where(unit => unit.DisplayName.StartsWith("演示·")).ToDictionary(unit => unit.DisplayName);
        demoDepartments.Count.ShouldBe(3);
        var root = demoDepartments["演示·示例公司"];
        var development = demoDepartments["演示·研发部"];
        var operations = demoDepartments["演示·运营部"];
        development.ParentId.ShouldBe(root.Id);
        operations.ParentId.ShouldBe(root.Id);
        (await departments.GetRolesAsync(development)).Select(role => role.Name).ShouldBe(new[] { ReaderRole });
        (await departments.GetRolesAsync(operations)).ShouldBeEmpty();

        var developerUser = await users.FindByNormalizedUserNameAsync("OU-DEMO-DEV");
        var operatorUser = await users.FindByNormalizedUserNameAsync("OU-DEMO-OPS");
        (await users.GetOrganizationUnitsAsync(developerUser.Id)).Select(unit => unit.Id).ShouldBe(new[] { development.Id });
        (await users.GetOrganizationUnitsAsync(operatorUser.Id)).Select(unit => unit.Id).ShouldBe(new[] { operations.Id });
        developerUser.Roles.ShouldBeEmpty();
        operatorUser.Roles.ShouldBeEmpty();

        var developerClaims = await WithUnitOfWorkAsync(async () =>
            await manager.GetClaimsAsync(await users.GetAsync(developerUser.Id)));
        developerClaims.Single(claim => claim.Type == ProjectCode).Value.ShouldBe("DEMO-BETA");
        developerClaims.Single(claim => claim.Type == "demo.badge_label").Value.ShouldBe("研发体验");

        var grants = GetRequiredService<IPermissionGrantRepository>();
        (await grants.GetListAsync()).Where(grant => grant.ProviderName == "R" && grant.ProviderKey == ReaderRole)
            .Select(grant => grant.Name).ShouldBe(new[] { "AbpAdmin.OrganizationUnits" });
    }
}
