using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Permissions;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.SignalR;

public class ChainPushAudienceTests
{
    [RequiresHostDatabaseFact]
    public async Task Routing_Follows_Committed_Host_Users_Roles_Organizations_And_Client_Grants()
    {
        var app = await HostTestBed.HostTask;
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var audience = services.GetRequiredService<IChainPushAudience>();
        var units = services.GetRequiredService<IUnitOfWorkManager>();
        var databases = services.GetRequiredService<IDbContextProvider<AbpAdminDbContext>>();
        var suffix = Guid.NewGuid().ToString("N");
        var role = new IdentityRole(Guid.NewGuid(), "role-" + suffix);
        var replacement = new IdentityRole(Guid.NewGuid(), role.Name);
        var unit = new OrganizationUnit(Guid.NewGuid(), "unit-" + suffix);
        var client = "client-" + suffix;
        const string permission = CollectionPermissions.Network.Admin;
        IdentityUser User(string prefix, Guid? tenant = null) =>
            new(Guid.NewGuid(), prefix + suffix, prefix + suffix + "@test.local", tenant);

        var member = User("member");
        member.AddRole(role.Id);
        var inherited = User("inherited");
        inherited.AddOrganizationUnit(unit.Id);
        unit.AddRole(role.Id);
        var direct = User("direct");
        var other = User("other");
        var disabled = User("disabled");
        disabled.SetIsActive(false);
        var foreign = User("tenant", Guid.NewGuid());
        var roleGrant = new PermissionGrant(Guid.NewGuid(), permission, "R", role.Name);
        var directGrant = new PermissionGrant(Guid.NewGuid(), permission, "U", direct.Id.ToString());
        var clientGrant = new PermissionGrant(Guid.NewGuid(), permission, "C", client);

        async Task WriteAsync(Func<AbpAdminDbContext, Task> write)
        {
            using var transaction = units.Begin(requiresNew: true, isTransactional: true);
            var db = await databases.GetDbContextAsync();
            await write(db);
            await transaction.CompleteAsync();
        }

        await WriteAsync(async db =>
        {
            await services.GetRequiredService<OrganizationUnitManager>().CreateAsync(unit);
            db.AddRange(role, member, inherited, direct, other, disabled, foreign, roleGrant, directGrant, clientGrant);
        });
        try
        {
            var before = await audience.GetGroupsAsync([permission]);
            foreach (var user in new[] { member, inherited, direct })
            {
                before.ShouldContain(ChainHubGroups.ForRecipient(user.Id, null));
                before.ShouldNotContain(ChainHubGroups.ForRecipient(user.Id, client));
            }
            before.ShouldContain(ChainHubGroups.ForRecipient(other.Id, client));
            before.ShouldNotContain(ChainHubGroups.ForRecipient(other.Id, null));
            foreach (var user in new[] { disabled, foreign })
            {
                before.ShouldNotContain(ChainHubGroups.ForRecipient(user.Id, client));
                before.ShouldNotContain(ChainHubGroups.ForRecipient(user.Id, null));
            }

            // The old role name now belongs to a different role ID. Members retain only their actual bindings.
            await WriteAsync(async db =>
            {
                await db.Roles.Where(r => r.Id == role.Id).ExecuteUpdateAsync(update =>
                    update.SetProperty(r => r.Name, role.Name + "-new").SetProperty(r => r.NormalizedName, role.NormalizedName + "-NEW"));
                var reassigned = await db.Users.Include(u => u.Roles).SingleAsync(u => u.Id == other.Id);
                reassigned.AddRole(replacement.Id);
                db.Roles.Add(replacement);
                var user = await db.Users.SingleAsync(u => u.Id == direct.Id);
                user.SetIsActive(false);
                await db.Set<PermissionGrant>().Where(g => g.Id == clientGrant.Id).ExecuteDeleteAsync();
            });
            var after = await audience.GetGroupsAsync([permission]);
            after.ShouldContain(ChainHubGroups.ForRecipient(other.Id, null));
            foreach (var user in new[] { member, inherited, direct, other })
                after.ShouldNotContain(ChainHubGroups.ForRecipient(user.Id, client));
            foreach (var user in new[] { member, inherited, direct })
                after.ShouldNotContain(ChainHubGroups.ForRecipient(user.Id, null));
        }
        finally
        {
            await WriteAsync(async db =>
            {
                var userIds = new[] { member.Id, inherited.Id, direct.Id, other.Id, disabled.Id, foreign.Id };
                await db.Set<PermissionGrant>().Where(g => g.Id == roleGrant.Id || g.Id == directGrant.Id || g.Id == clientGrant.Id)
                    .ExecuteDeleteAsync();
                await db.Users.IgnoreQueryFilters().Where(u => userIds.Contains(u.Id)).ExecuteDeleteAsync();
                await db.OrganizationUnits.Where(u => u.Id == unit.Id).ExecuteDeleteAsync();
                await db.Roles.Where(r => r.Id == role.Id || r.Id == replacement.Id).ExecuteDeleteAsync();
            });
        }
    }
}
