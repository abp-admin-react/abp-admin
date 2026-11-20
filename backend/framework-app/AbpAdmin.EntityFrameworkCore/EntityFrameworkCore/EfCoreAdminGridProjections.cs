using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Grids;
using AbpAdmin.Tenants;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.EntityFrameworkCore;

public sealed class EfCoreAdminGridProjections(IDbContextProvider<AbpAdminDbContext> provider)
    : IAdminGridProjections, ITransientDependency
{
    private sealed class ExtensionValue
    {
        public Guid Id { get; set; }
        public string? Value { get; set; }
    }

    public async Task<IQueryable<RoleGridRecord>> RolesAsync()
    {
        var db = await provider.GetDbContextAsync();
        // ExtraProperties 使用 JSON 字符串存储，直接读取一个键；主查询保留角色的全局过滤器。
        var remarks = db.Database.SqlQuery<ExtensionValue>(
            $"""SELECT "Id", "ExtraProperties"::jsonb ->> 'Remark' AS "Value" FROM "AbpRoles" """);
        return from role in db.Roles.AsNoTracking()
               join remark in remarks on role.Id equals remark.Id
               select new RoleGridRecord { Role = role, Remark = remark.Value };
    }

    public async Task<IQueryable<TenantGridRecord>> TenantsAsync(bool canReadEditionNames)
    {
        var db = await provider.GetDbContextAsync();
        var editionIds = db.Database.SqlQuery<ExtensionValue>(
            $"""SELECT "Id", "ExtraProperties"::jsonb ->> 'EditionId' AS "Value" FROM "AbpTenants" """);
        return from tenant in db.Tenants.AsNoTracking()
               join property in editionIds on tenant.Id equals property.Id
               join edition in db.Editions.Where(x => canReadEditionNames) on property.Value equals edition.Id.ToString() into editions
               from edition in editions.DefaultIfEmpty()
               select new TenantGridRecord
               {
                   Tenant = tenant,
                   Edition = edition == null ? property.Value : edition.DisplayName,
                   ActivationState = EF.Property<byte>(tenant, AbpAdminTenantConsts.ActivationStatePropertyName),
                   EditionEnd = EF.Property<DateTime?>(tenant, AbpAdminTenantConsts.EditionEndDateUtcPropertyName)
               };
    }
}
