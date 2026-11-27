using System;
using System.Threading.Tasks;
using AbpAdmin.Identity;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.EntityFrameworkCore.Applications;

[Collection(AbpAdminTestConsts.CollectionDefinitionName)]
public class RolePermissionTargetTests : AbpAdminEntityFrameworkCoreTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejects_A_Stale_Name_Foreign_Tenant_And_Legacy_Duplicates(bool forUpdate)
    {
        // The shared test manager disables transactions. Its underlying ABP manager still
        // links the identity and grant DbContexts to the same production-style transaction.
        using var uow = GetRequiredService<UnitOfWorkManager>().Begin(
            new AbpUnitOfWorkOptions { IsTransactional = true }, requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<AbpAdminDbContext>>().GetDbContextAsync();
        var role = new IdentityRole(Guid.NewGuid(), "quality-reviewer");
        var foreign = new IdentityRole(Guid.NewGuid(), role.Name, Guid.NewGuid());
        db.Roles.AddRange(role, foreign);
        await db.SaveChangesAsync();

        var service = GetRequiredService<IRolePermissionAppService>();
        Task Access(Guid id, string name) => forUpdate
            ? service.UpdateAsync(id, new UpdateRolePermissionsDto { ExpectedRoleName = name, Permissions = [] })
            : service.GetAsync(id, name);
        await Access(role.Id, role.Name);
        var renamed = await Should.ThrowAsync<BusinessException>(() => Access(role.Id, "old-reviewer"));
        renamed.Code.ShouldBe(AbpAdminDomainErrorCodes.Identity.RolePermissionTargetChanged);
        await Should.ThrowAsync<EntityNotFoundException>(() => Access(foreign.Id, foreign.Name));
        await Should.ThrowAsync<EntityNotFoundException>(() => Access(Guid.NewGuid(), role.Name));
        using (GetRequiredService<ICurrentTenant>().Change(foreign.TenantId))
        {
            await Access(foreign.Id, foreign.Name);
            await Should.ThrowAsync<EntityNotFoundException>(() => Access(role.Id, role.Name));
        }

        db.Roles.Add(new IdentityRole(Guid.NewGuid(), role.Name.ToUpperInvariant()));
        await db.SaveChangesAsync();
        var duplicate = await Should.ThrowAsync<BusinessException>(() => Access(role.Id, role.Name));
        duplicate.Code.ShouldBe(AbpAdminDomainErrorCodes.Identity.RolePermissionNameAmbiguous);
        await uow.CompleteAsync();
    }
}
