using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Xunit;

namespace AbpAdmin.DataScopes;

/// <summary>从真实角色范围、用户部门及部门树解析快照，避免只测试预先构造的快照。</summary>
public abstract class CurrentDataScopeProviderTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly ICurrentDataScopeProvider _provider;
    private readonly IRepository<RoleDataScope, Guid> _scopeRepository;
    private readonly IIdentityRoleRepository _roleRepository;
    private readonly OrganizationUnitManager _organizationManager;
    private readonly IdentityUserManager _userManager;
    private readonly ICurrentTenant _tenant;
    private readonly ICurrentPrincipalAccessor _principal;

    protected CurrentDataScopeProviderTests()
    {
        _provider = GetRequiredService<ICurrentDataScopeProvider>();
        _scopeRepository = GetRequiredService<IRepository<RoleDataScope, Guid>>();
        _roleRepository = GetRequiredService<IIdentityRoleRepository>();
        _organizationManager = GetRequiredService<OrganizationUnitManager>();
        _userManager = GetRequiredService<IdentityUserManager>();
        _tenant = GetRequiredService<ICurrentTenant>();
        _principal = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    private async Task<RoleDataScope> CreateScopeAsync(DataScopeTypeEnum type, Guid[]? organizationIds = null, string? roleName = null)
    {
        roleName ??= $"scope-{Guid.NewGuid():N}";
        var scope = new RoleDataScope(Guid.NewGuid(), roleName, type, _tenant.Id);
        foreach (var id in organizationIds ?? [])
        {
            scope.CustomOrganizationUnits.Add(new RoleDataScopeOrganizationUnit(scope.Id, id));
        }
        await WithUnitOfWorkAsync(async () =>
        {
            await _roleRepository.InsertAsync(new IdentityRole(Guid.NewGuid(), roleName, _tenant.Id));
            await _scopeRepository.InsertAsync(scope);
        });
        return scope;
    }

    private async Task<IdentityUser> CreateUserAsync()
    {
        var name = $"scope-user-{Guid.NewGuid():N}";
        var user = new IdentityUser(Guid.NewGuid(), name, $"{name}@test.com", _tenant.Id);
        await WithUnitOfWorkAsync(async () =>
            (await _userManager.CreateAsync(user, "Test@123456")).Succeeded.ShouldBeTrue());
        return user;
    }

    private async Task<OrganizationUnit> CreateOrganizationAsync(Guid? parentId = null)
    {
        var unit = new OrganizationUnit(Guid.NewGuid(), $"Scope-{Guid.NewGuid():N}", parentId, _tenant.Id);
        await WithUnitOfWorkAsync(() => _organizationManager.CreateAsync(unit));
        return unit;
    }

    private async Task<DataScopeSnapshot> ResolveAsync(Guid userId, params string[] roleNames)
    {
        var claims = new List<Claim> { new(AbpClaimTypes.UserId, userId.ToString()) };
        claims.AddRange(roleNames.Select(role => new Claim(AbpClaimTypes.Role, role)));
        if (_tenant.Id.HasValue)
        {
            claims.Add(new Claim(AbpClaimTypes.TenantId, _tenant.Id.Value.ToString()));
        }
        using (_principal.Change(new ClaimsPrincipal(new ClaimsIdentity(claims, "Testing"))))
        {
            return await WithUnitOfWorkAsync(() => _provider.GetAsync());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_Roles_Or_Unconfigured_Roles_Are_Fail_Closed(bool hasUnconfiguredRole)
    {
        var user = await CreateUserAsync();
        var snapshot = await ResolveAsync(user.Id, hasUnconfiguredRole ? ["unconfigured"] : []);
        snapshot.IsAll.ShouldBeFalse();
        snapshot.SelfOnly.ShouldBeFalse();
        snapshot.OrganizationUnitIds.ShouldBeEmpty();
    }

    [Fact]
    public async Task Admin_Does_Not_Need_A_Separate_Data_Scope_Configuration()
    {
        var user = await CreateUserAsync();
        (await ResolveAsync(user.Id, "admin")).IsAll.ShouldBeTrue();
    }

    [Fact]
    public async Task All_Role_Overrides_Restricted_Roles()
    {
        var user = await CreateUserAsync();
        var all = await CreateScopeAsync(DataScopeTypeEnum.All);
        var self = await CreateScopeAsync(DataScopeTypeEnum.SelfOnly);
        (await ResolveAsync(user.Id, self.RoleName, all.RoleName)).IsAll.ShouldBeTrue();
    }

    [Fact]
    public async Task Custom_Organization_And_Self_Roles_Are_Combined_Without_Duplicates()
    {
        var user = await CreateUserAsync();
        var first = await CreateOrganizationAsync();
        var second = await CreateOrganizationAsync();
        var customFirst = await CreateScopeAsync(DataScopeTypeEnum.Custom, [first.Id]);
        var customBoth = await CreateScopeAsync(DataScopeTypeEnum.Custom, [first.Id, second.Id]);
        var self = await CreateScopeAsync(DataScopeTypeEnum.SelfOnly);
        var snapshot = await ResolveAsync(user.Id, customFirst.RoleName, self.RoleName, customBoth.RoleName);
        snapshot.IsAll.ShouldBeFalse();
        snapshot.SelfOnly.ShouldBeTrue();
        snapshot.UserId.ShouldBe(user.Id);
        snapshot.OrganizationUnitIds.OrderBy(id => id).ShouldBe(new[] { first.Id, second.Id }.OrderBy(id => id));
    }

    [Theory]
    [InlineData(DataScopeTypeEnum.CurrentOu)]
    [InlineData(DataScopeTypeEnum.CurrentOuAndChildren)]
    public async Task Current_Organization_Uses_All_User_Memberships_And_Only_Their_Subtrees(DataScopeTypeEnum type)
    {
        var user = await CreateUserAsync();
        var first = await CreateOrganizationAsync();
        var child = await CreateOrganizationAsync(first.Id);
        var second = await CreateOrganizationAsync();
        var other = await CreateOrganizationAsync();
        var memberships = new[] { first.Id, second.Id };
        await WithUnitOfWorkAsync(() => _userManager.SetOrganizationUnitsAsync(user.Id, memberships));
        await WithUnitOfWorkAsync(async () =>
        {
            var storedUser = await _userManager.GetByIdAsync(user.Id);
            var storedUnits = await _userManager.GetOrganizationUnitsAsync(storedUser);
            storedUnits.Select(unit => unit.Id).OrderBy(id => id).ShouldBe(memberships.OrderBy(id => id));
        });
        var role = await CreateScopeAsync(type);
        var snapshot = await ResolveAsync(user.Id, role.RoleName);
        var expected = type == DataScopeTypeEnum.CurrentOuAndChildren
            ? new[] { first.Id, child.Id, second.Id }
            : new[] { first.Id, second.Id };
        snapshot.OrganizationUnitIds.OrderBy(id => id).ShouldBe(expected.OrderBy(id => id));
        snapshot.OrganizationUnitIds.ShouldNotContain(other.Id);
        snapshot.IsAll.ShouldBeFalse();
    }

    [Fact]
    public async Task Same_Role_Name_Does_Not_Read_Another_Tenants_Data_Scope()
    {
        var roleName = $"shared-{Guid.NewGuid():N}";
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        using (_tenant.Change(tenantA))
        {
            await CreateScopeAsync(DataScopeTypeEnum.All, roleName: roleName);
        }
        using (_tenant.Change(tenantB))
        {
            var user = await CreateUserAsync();
            await CreateScopeAsync(DataScopeTypeEnum.SelfOnly, roleName: roleName);
            var snapshot = await ResolveAsync(user.Id, roleName);
            snapshot.IsAll.ShouldBeFalse();
            snapshot.SelfOnly.ShouldBeTrue();
            snapshot.OrganizationUnitIds.ShouldBeEmpty();
        }
    }

    [Fact]
    public async Task Changed_Role_Scope_Invalidates_Previously_Cached_Snapshot()
    {
        var user = await CreateUserAsync();
        var role = await CreateScopeAsync(DataScopeTypeEnum.SelfOnly);
        (await ResolveAsync(user.Id, role.RoleName)).IsAll.ShouldBeFalse();
        await WithUnitOfWorkAsync(async () =>
        {
            var stored = await _scopeRepository.GetAsync(role.Id);
            stored.SetScopeType(DataScopeTypeEnum.All);
            await _scopeRepository.UpdateAsync(stored);
        });
        (await ResolveAsync(user.Id, role.RoleName)).IsAll.ShouldBeTrue();
    }
}
