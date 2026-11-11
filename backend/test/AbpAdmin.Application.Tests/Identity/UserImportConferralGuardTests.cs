using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.DataScopes;
using AbpAdmin.Localization;
using Microsoft.Extensions.Localization;
using MiniExcelLibs;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Content;
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
    private readonly IIdentityUserAdminAppService _importService;
    private readonly OrganizationUnitManager _organizationManager;
    private readonly IdentityUserManager _userManager;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly IRepository<RoleDataScope, Guid> _scopeRepository;
    private readonly ICurrentDataScopeProvider _scopeProvider;
    private readonly ICurrentPrincipalAccessor _principal;
    private readonly IStringLocalizer<AbpAdminResource> _localizer;

    protected UserImportConferralGuardTests()
    {
        _organizationService = GetRequiredService<IOrganizationUnitAppService>();
        _importService = GetRequiredService<IIdentityUserAdminAppService>();
        _organizationManager = GetRequiredService<OrganizationUnitManager>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _roleRepository = GetRequiredService<IIdentityRoleRepository>();
        _scopeRepository = GetRequiredService<IRepository<RoleDataScope, Guid>>();
        _scopeProvider = GetRequiredService<ICurrentDataScopeProvider>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
        _localizer = GetRequiredService<IStringLocalizer<AbpAdminResource>>();
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

    [Theory]
    [InlineData(false, "admin")]
    [InlineData(true, "admin")]
    [InlineData(false, "finance-boss")]
    [InlineData(true, "finance-boss")]
    public async Task Import_With_All_Data_Scope_Rejects_Foreign_Roles_And_Continues_Own_Role_Rows(
        bool throughDepartment, string targetRoleName)
    {
        var ownRole = await CreateAllDataRoleAsync();
        var foreignRole = await EnsureRoleAsync(targetRoleName);
        var foreignDepartment = await CreateDepartmentAsync(foreignRole);
        var ownDepartment = await CreateDepartmentAsync(ownRole);
        var rejected = ImportRow();
        rejected.RoleNames = throughDepartment ? null! : foreignRole.Name;
        rejected.OrganizationUnitCodes = throughDepartment ? foreignDepartment.Code : null!;
        var direct = ImportRow();
        direct.RoleNames = ownRole.Name;
        var inherited = ImportRow();
        inherited.OrganizationUnitCodes = ownDepartment.Code;

        using (AsOperator(ownRole.Name))
        {
            await WithUnitOfWorkAsync(async () => (await _scopeProvider.GetAsync()).IsAll.ShouldBeTrue());
            var result = await ImportAsync(rejected, direct, inherited);
            result.TotalCount.ShouldBe(3);
            result.FailedCount.ShouldBe(1);
            result.SucceededCount.ShouldBe(2);
            result.Errors.Single().RowNumber.ShouldBe(1);
            result.Errors.Single().ErrorMessage.ShouldBe(throughDepartment
                ? _localizer["UserImport:OuRoleBeyondAuthority", 1, foreignDepartment.Code, foreignRole.Name].Value
                : _localizer["UserImport:RoleBeyondAuthority", 1, foreignRole.Name].Value);
        }

        await WithUnitOfWorkAsync(async () =>
        {
            var directlyAssigned = await _userManager.FindByNameAsync(direct.UserName!);
            var departmentAssigned = await _userManager.FindByNameAsync(inherited.UserName!);
            directlyAssigned.ShouldNotBeNull();
            departmentAssigned.ShouldNotBeNull();
            (await _userManager.GetRolesAsync(directlyAssigned)).ShouldContain(ownRole.Name);
            (await _userManager.GetRolesAsync(departmentAssigned)).ShouldContain(ownRole.Name);
            // 测试基座禁用事务；即使失败用户已暂存，也绝不能得到被拒绝的角色或部门成员资格。
            var rejectedUser = await _userManager.FindByNameAsync(rejected.UserName!);
            if (rejectedUser != null)
            {
                (await _userManager.GetRolesAsync(rejectedUser)).ShouldNotContain(foreignRole.Name);
                (await _userManager.GetOrganizationUnitsAsync(rejectedUser)).ShouldBeEmpty();
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Admin_Can_Import_Foreign_Roles_Through_Both_Channels(bool throughDepartment)
    {
        var foreignRole = await EnsureRoleAsync("finance-boss");
        var department = await CreateDepartmentAsync(foreignRole);
        var row = ImportRow();
        row.RoleNames = throughDepartment ? null! : foreignRole.Name;
        row.OrganizationUnitCodes = throughDepartment ? department.Code : null!;
        using (AsOperator("admin"))
        {
            var result = await ImportAsync(row);
            result.FailedCount.ShouldBe(0);
            result.SucceededCount.ShouldBe(1);
        }
        await WithUnitOfWorkAsync(async () =>
        {
            var user = await _userManager.FindByNameAsync(row.UserName!);
            user.ShouldNotBeNull();
            (await _userManager.GetRolesAsync(user)).ShouldContain(foreignRole.Name);
        });
    }

    private static UserImportRowDto ImportRow()
    {
        var name = $"import-{Guid.NewGuid():N}";
        return new UserImportRowDto { UserName = name, Email = $"{name}@test.com", Password = "Test@123456" };
    }

    private async Task<UserImportResultDto> ImportAsync(params UserImportRowDto[] rows)
    {
        using var stream = new MemoryStream();
        MiniExcel.SaveAs(stream, rows);
        stream.Position = 0;
        return await _importService.ImportAsync(new RemoteStreamContent(stream, "roles.xlsx"));
    }
}
