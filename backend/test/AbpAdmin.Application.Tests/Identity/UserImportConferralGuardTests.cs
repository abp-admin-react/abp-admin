using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.DataScopes;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Xunit;

namespace AbpAdmin.Identity;

/// <summary>真实身份服务回归：业务 All 范围不能绕过角色授予边界。</summary>
public abstract class UserImportConferralGuardTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IOrganizationUnitAppService _organizationService;
    private readonly OrganizationUnitManager _organizationManager;
    private readonly IdentityUserManager _userManager;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IRepository<RoleDataScope, Guid> _scopeRepository;
    private readonly ICurrentDataScopeProvider _scopeProvider;
    private readonly ICurrentPrincipalAccessor _principal;

    protected UserImportConferralGuardTests()
    {
        _organizationService = GetRequiredService<IOrganizationUnitAppService>();
        _organizationManager = GetRequiredService<OrganizationUnitManager>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _roleRepository = GetRequiredService<IIdentityRoleRepository>();
        _scopeRepository = GetRequiredService<IRepository<RoleDataScope, Guid>>();
        _scopeProvider = GetRequiredService<ICurrentDataScopeProvider>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    private IDisposable AsOperator(params string[] roles)
    {
        var claims = new List<Claim> { new(AbpClaimTypes.UserId, Guid.NewGuid().ToString()) };
        claims.AddRange(roles.Select(role => new Claim(AbpClaimTypes.Role, role)));
        return _principal.Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "Testing")));
    }

    private async Task<IdentityRole> EnsureRoleAsync(string name)
    {
        return await WithUnitOfWorkAsync(async () =>
            await _roleRepository.FindByNormalizedNameAsync(name.ToUpperInvariant())
            ?? await _roleRepository.InsertAsync(new IdentityRole(Guid.NewGuid(), name), autoSave: true));
    }

    private async Task<IdentityRole> CreateAllDataRoleAsync()
    {
        var role = await EnsureRoleAsync($"all-data-{Guid.NewGuid():N}");
        await WithUnitOfWorkAsync(() => _scopeRepository.InsertAsync(
            new RoleDataScope(Guid.NewGuid(), role.Name, DataScopeTypeEnum.All), autoSave: true));
        return role;
    }

    private async Task<OrganizationUnit> CreateDepartmentAsync(IdentityRole? role = null)
    {
        var department = new OrganizationUnit(Guid.NewGuid(), $"Department-{Guid.NewGuid():N}");
        await WithUnitOfWorkAsync(async () =>
        {
            await _organizationManager.CreateAsync(department);
        });
        if (role != null)
        {
            await WithUnitOfWorkAsync(() =>
                _organizationManager.AddRoleToOrganizationUnitAsync(role.Id, department.Id));
        }
        return department;
    }

    private async Task<IdentityUser> CreateMemberAsync()
    {
        var name = $"member-{Guid.NewGuid():N}";
        var user = new IdentityUser(Guid.NewGuid(), name, $"{name}@test.com");
        await WithUnitOfWorkAsync(async () =>
            (await _userManager.CreateAsync(user, "Test@123456")).Succeeded.ShouldBeTrue());
        return user;
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("finance-boss")]
    public async Task All_Data_Operator_Cannot_Attach_Foreign_Roles_To_Department(string targetRoleName)
    {
        var ownRole = await CreateAllDataRoleAsync();
        var foreignRole = await EnsureRoleAsync(targetRoleName);
        var department = await CreateDepartmentAsync();
        using (AsOperator(ownRole.Name))
        {
            await WithUnitOfWorkAsync(async () => (await _scopeProvider.GetAsync()).IsAll.ShouldBeTrue());
            var exception = await Should.ThrowAsync<BusinessException>(() =>
                _organizationService.AddRolesAsync(department.Id, [foreignRole.Id]));
            exception.Code.ShouldBe(ClientErrorCodes.Forbidden);
            (await _organizationService.GetRolesAsync(department.Id)).Items.ShouldBeEmpty();
        }
    }

    [Theory]
    [InlineData("admin")]
    [InlineData("finance-boss")]
    public async Task All_Data_Operator_Cannot_Confer_Foreign_Roles_By_Adding_Department_Members(string targetRoleName)
    {
        var ownRole = await CreateAllDataRoleAsync();
        var foreignRole = await EnsureRoleAsync(targetRoleName);
        var department = await CreateDepartmentAsync(foreignRole);
        var member = await CreateMemberAsync();
        using (AsOperator(ownRole.Name))
        {
            await WithUnitOfWorkAsync(async () => (await _scopeProvider.GetAsync()).IsAll.ShouldBeTrue());
            var exception = await Should.ThrowAsync<BusinessException>(() =>
                _organizationService.AddMembersAsync(department.Id, [member.Id]));
            exception.Code.ShouldBe(ClientErrorCodes.Forbidden);
            await WithUnitOfWorkAsync(async () => (await _userManager.GetOrganizationUnitsAsync(member)).ShouldBeEmpty());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Own_Roles_And_Admin_Still_Allow_Department_Conferral(bool isAdmin)
    {
        var ownRole = await CreateAllDataRoleAsync();
        var grantedRole = isAdmin ? await EnsureRoleAsync("finance-boss") : ownRole;
        var department = await CreateDepartmentAsync();
        var member = await CreateMemberAsync();
        using (AsOperator(isAdmin ? "admin" : ownRole.Name))
        {
            await _organizationService.AddRolesAsync(department.Id, [grantedRole.Id]);
            await _organizationService.AddMembersAsync(department.Id, [member.Id]);
        }
        await WithUnitOfWorkAsync(async () => (await _userManager.GetRolesAsync(member)).ShouldContain(grantedRole.Name));
    }

}
