using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SimpleStateChecking;

namespace AbpAdmin.Identity;

[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IPermissionManager), typeof(PermissionManager))]
public class AbpAdminPermissionManager(
    IPermissionDefinitionManager definitions, ISimpleStateCheckerManager<PermissionDefinition> stateCheckers,
    IPermissionGrantRepository grants, IServiceProvider services, IGuidGenerator guids,
    IOptions<PermissionManagementOptions> options, ICurrentTenant currentTenant, IDistributedCache<PermissionGrantCacheItem> cache)
    : PermissionManager(definitions, stateCheckers, grants, services, guids, options, currentTenant, cache)
{
    public override async Task<PermissionGrant> UpdateProviderKeyAsync(PermissionGrant grant, string providerKey)
    {
        var oldProviderKey = grant.ProviderKey;
        var result = await base.UpdateProviderKeyAsync(grant, providerKey);
        if (grant.ProviderName == RolePermissionValueProvider.ProviderName && oldProviderKey != providerKey)
        {
            // ABP evicts the old key before the rename commits. A concurrent authorization check can
            // still read the old committed grant and cache it again. Evict it after commit as well.
            using (CurrentTenant.Change(grant.TenantId))
            {
                var key = PermissionGrantCacheItem.CalculateCacheKey(grant.Name, grant.ProviderName, oldProviderKey);
                await Cache.RemoveAsync(key, considerUow: true);
            }
        }

        return result;
    }
}
