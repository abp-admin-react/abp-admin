using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Controllers;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Authorization;

public class RolePermissionReadTransactionTests
{
    [RequiresHostDatabaseFact]
    public async Task Controller_Reads_Grants_When_Authenticated_Get_Already_Has_A_Nontransactional_Unit()
    {
        var app = await HostTestBed.HostTask;
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        using var request = services.GetRequiredService<IUnitOfWorkManager>().Begin(
            requiresNew: true, isTransactional: false);
        var admin = (await services.GetRequiredService<IdentityUserManager>().FindByNameAsync("admin")).ShouldNotBeNull();
        var role = (await services.GetRequiredService<IdentityRoleManager>().FindByNameAsync("admin")).ShouldNotBeNull();
        var db = await services.GetRequiredService<IDbContextProvider<AbpAdminDbContext>>().GetDbContextAsync();
        db.Database.CurrentTransaction.ShouldBeNull();

        var principal = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(AbpClaimTypes.UserId, admin.Id.ToString()),
            new Claim(AbpClaimTypes.UserName, admin.UserName),
            new Claim(AbpClaimTypes.Role, role.Name),
        ], "Testing"));
        using var signedIn = services.GetRequiredService<ICurrentPrincipalAccessor>().Change(principal);
        var controller = ActivatorUtilities.CreateInstance<RolePermissionController>(services);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = services, User = principal },
        };
        var result = await controller.GetAsync(role.Id, role.Name);
        result.Groups.SelectMany(group => group.Permissions)
            .ShouldContain(permission => permission.Name == IdentityPermissions.Roles.ManagePermissions);
        db.Database.CurrentTransaction.ShouldBeNull();
        await request.CompleteAsync();
    }
}
