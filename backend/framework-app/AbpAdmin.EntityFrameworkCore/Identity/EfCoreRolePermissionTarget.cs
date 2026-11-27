using System;
using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.ExceptionHandling;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement.EntityFrameworkCore;
using Volo.Abp.Threading;

namespace AbpAdmin.Identity;

public sealed class EfCoreRolePermissionTarget(IDbContextProvider<AbpAdminDbContext> provider,
    IDbContextProvider<IPermissionManagementDbContext> grantProvider, ICurrentTenant currentTenant, ICancellationTokenProvider cancellation)
    : IRolePermissionTarget, ITransientDependency
{
    private sealed class RoleKey
    {
        public string Name { get; set; } = null!;
        public string NormalizedName { get; set; } = null!;
    }

    private sealed class MissingRole(Guid id) : EntityNotFoundException(typeof(IdentityRole), id), IHasErrorCode
    {
        public string Code => ClientErrorCodes.NotFound;
    }

    public async Task<string> LockAsync(Guid id, string expectedRoleName, bool forUpdate)
    {
        var db = await provider.GetDbContextAsync();
        var grants = await grantProvider.GetDbContextAsync();
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction();
        // ABP normally enlists both DbContexts in the same PostgreSQL transaction. A split-database
        // deployment cannot release the role lock atomically with its grant commit.
        if (transaction == null || !ReferenceEquals(transaction, grants.Database.CurrentTransaction?.GetDbTransaction()))
        {
            throw new InvalidOperationException("Role and permission storage must share the same active database transaction.");
        }

        // Scalar projection reads the locked row directly, including after waiting for a rename.
        // Never reuse an entity already tracked before the lock or rely on optional ABP data filters.
        var tenantId = currentTenant.Id;
        var locked = forUpdate
            ? db.Database.SqlQuery<RoleKey>($"""SELECT "Name", "NormalizedName" FROM "AbpRoles" WHERE "Id" = {id} AND "TenantId" IS NOT DISTINCT FROM {tenantId} FOR UPDATE""")
            : db.Database.SqlQuery<RoleKey>($"""SELECT "Name", "NormalizedName" FROM "AbpRoles" WHERE "Id" = {id} AND "TenantId" IS NOT DISTINCT FROM {tenantId} FOR SHARE""");
        var role = await locked.SingleOrDefaultAsync(cancellation.Token)
            ?? throw new MissingRole(id);
        if (!string.Equals(role.Name, expectedRoleName, StringComparison.Ordinal))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Identity.RolePermissionTargetChanged);
        }

        // Two roles with the same normalized name in this tenant make the name-based grant target ambiguous.
        if (await db.Roles.AnyAsync(other => other.TenantId == tenantId && other.Id != id &&
            other.NormalizedName == role.NormalizedName, cancellation.Token))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Identity.RolePermissionNameAmbiguous);
        }

        return role.Name;
    }
}
