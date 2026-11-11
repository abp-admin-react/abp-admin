using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Identity;

public class IdentityUiDemoSeedContributor(
    IConfiguration configuration,
    ICurrentTenant currentTenant,
    IGuidGenerator guids,
    IUnitOfWorkManager unitOfWorkManager,
    IOrganizationUnitRepository departments,
    OrganizationUnitManager departmentManager,
    IIdentityRoleRepository roles,
    IdentityRoleManager roleManager,
    IIdentityUserRepository users,
    IdentityUserManager userManager,
    IIdentityClaimTypeRepository claimTypes,
    IdentityClaimTypeManager claimTypeManager,
    IPermissionDataSeeder permissionSeeder) : IDataSeedContributor, ITransientDependency
{
    private const string DemoMarker = "AbpAdmin.IdentityUiDemo";
    private const string ReaderRole = "ou-demo-reader";
    private const string ProjectCode = "demo.project_code";
    private const string SupportRegion = "demo.support_region";
    private const string BadgeLabel = "demo.badge_label";

    public virtual async Task SeedAsync(DataSeedContext context)
    {
        if (context.TenantId is not null || !configuration.GetValue<bool>("App:SeedIdentityUiDemo"))
        {
            return;
        }

        using var host = currentTenant.Change(null);
        // DbMigrator 的外层种子工作单元默认不启事务，单独开启事务以原子地创建整组演示数据。
        using var uow = unitOfWorkManager.Begin(new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true);
        var developer = await EnsureUserAsync("ou-demo-dev", "演示小林");
        var operatorUser = await EnsureUserAsync("ou-demo-ops", "演示小周");
        var reader = await EnsureReaderRoleAsync();
        var root = await EnsureDepartmentAsync("演示·示例公司");
        var development = await EnsureDepartmentAsync("演示·研发部", root.Id);
        var operations = await EnsureDepartmentAsync("演示·运营部", root.Id);

        await permissionSeeder.SeedAsync("R", reader.Name, ["AbpAdmin.OrganizationUnits"]);
        await departmentManager.AddRoleToOrganizationUnitAsync(reader.Id, development.Id);
        await userManager.AddToOrganizationUnitAsync(developer.Id, development.Id);
        await userManager.AddToOrganizationUnitAsync(operatorUser.Id, operations.Id);

        await EnsureClaimTypeAsync(ProjectCode, "演示项目代号，例如 DEMO-ALPHA。");
        await EnsureClaimTypeAsync(SupportRegion, "演示服务区域，例如 east 或 west。");
        await EnsureClaimTypeAsync(BadgeLabel, "演示页面标签，例如「研发体验」。");

        var existingClaims = await userManager.GetClaimsAsync(developer);
        var demoClaims = new[] { new Claim(ProjectCode, "DEMO-ALPHA"), new Claim(BadgeLabel, "研发体验") };
        var missingClaims = demoClaims
            .Where(claim => !existingClaims.Any(existing => string.Equals(existing.Type, claim.Type, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (missingClaims.Length > 0)
        {
            (await userManager.AddClaimsAsync(developer, missingClaims)).CheckErrors();
        }

        await uow.CompleteAsync();
    }

    private async Task<IdentityUser> EnsureUserAsync(string userName, string name)
    {
        var existing = (await users.GetUsersByNormalizedUserNameAsync(userName.ToUpperInvariant()))
            .SingleOrDefault(user => user.TenantId is null);
        if (existing is not null)
        {
            return RequireDemoOwner(existing, userName);
        }

        var password = configuration["App:IdentityUiDemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Set App:IdentityUiDemoPassword before creating the identity demo accounts.");
        }

        var user = new IdentityUser(guids.Create(), userName, userName + "@example.test") { Name = name };
        user.SetProperty(DemoMarker, true);
        (await userManager.CreateAsync(user, password)).CheckErrors();
        return user;
    }

    private async Task<IdentityRole> EnsureReaderRoleAsync()
    {
        var existing = (await roles.GetListAsync(filter: ReaderRole))
            .SingleOrDefault(role => role.TenantId is null && string.Equals(role.Name, ReaderRole, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return RequireDemoOwner(existing, ReaderRole);
        }

        var role = new IdentityRole(guids.Create(), ReaderRole) { IsDefault = false, IsPublic = false };
        role.SetProperty(DemoMarker, true);
        role.SetProperty(RoleRemarkConsts.PropertyName, "部门演示专用，可查看部门列表和成员。通过研发部统一角色授予。");
        (await roleManager.CreateAsync(role)).CheckErrors();
        return role;
    }

    private async Task<OrganizationUnit> EnsureDepartmentAsync(string name, Guid? parentId = null)
    {
        var existing = (await departments.GetChildrenAsync(parentId))
            .SingleOrDefault(unit => unit.TenantId is null && unit.DisplayName == name);
        if (existing is not null)
        {
            return RequireDemoOwner(existing, name);
        }

        var department = new OrganizationUnit(guids.Create(), name, parentId);
        department.SetProperty(DemoMarker, true);
        await departmentManager.CreateAsync(department);
        // ABP 为子节点和兄弟节点分配编码时，会先从数据库读取已创建的节点。
        await unitOfWorkManager.Current!.SaveChangesAsync();
        return department;
    }

    private async Task EnsureClaimTypeAsync(string name, string description)
    {
        if (!await claimTypes.AnyAsync(name))
        {
            await claimTypeManager.CreateAsync(new IdentityClaimType(
                guids.Create(), name, false, false, null, null, description, IdentityClaimValueType.String));
        }
    }

    private static T RequireDemoOwner<T>(T entity, string name) where T : IHasExtraProperties
    {
        if (!entity.GetProperty<bool>(DemoMarker))
        {
            throw new InvalidOperationException($"Existing record '{name}' is not owned by the identity UI demo seed.");
        }

        return entity;
    }
}
