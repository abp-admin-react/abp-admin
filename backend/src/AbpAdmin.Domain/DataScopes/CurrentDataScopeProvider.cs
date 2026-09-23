using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Linq;
using Volo.Abp.Users;

namespace AbpAdmin.DataScopes;

/// <summary>
/// <see cref="ICurrentDataScopeProvider"/> 的默认实现。
/// 解析链：角色名 → RoleDataScope → 按类型求并集 → OU 子树展开 → 缓存。
/// </summary>
public class CurrentDataScopeProvider : ICurrentDataScopeProvider, ITransientDependency
{
    private readonly ICurrentUser _currentUser;
    private readonly IIdentityUserRepository _userRepository;
    private readonly IRepository<OrganizationUnit, Guid> _organizationUnitQueryRepository;
    private readonly IRepository<RoleDataScope, Guid> _roleDataScopeRepository;
    private readonly IDistributedCache<DataScopeCacheItem> _cache;
    private readonly IDataScopeCacheGeneration _generation;
    private readonly IAsyncQueryableExecuter _asyncExecuter;

    public CurrentDataScopeProvider(
        ICurrentUser currentUser,
        IIdentityUserRepository userRepository,
        IRepository<OrganizationUnit, Guid> organizationUnitQueryRepository,
        IRepository<RoleDataScope, Guid> roleDataScopeRepository,
        IDistributedCache<DataScopeCacheItem> cache,
        IDataScopeCacheGeneration generation,
        IAsyncQueryableExecuter asyncExecuter)
    {
        _currentUser = currentUser;
        _userRepository = userRepository;
        _organizationUnitQueryRepository = organizationUnitQueryRepository;
        _roleDataScopeRepository = roleDataScopeRepository;
        _cache = cache;
        _generation = generation;
        _asyncExecuter = asyncExecuter;
    }

    public async Task<DataScopeSnapshot> GetAsync()
    {
        if (!_currentUser.IsAuthenticated || _currentUser.Id == null)
        {
            return new DataScopeSnapshot(false, false, Array.Empty<Guid>(), null);
        }

        var userId = _currentUser.Id.Value;
        var gen = await _generation.GetAsync();
        var cacheKey = BuildCacheKey(gen, userId);

        var cacheItem = await _cache.GetOrAddAsync(
            cacheKey,
            async () => DataScopeCacheItem.FromSnapshot(await ResolveAsync(userId)),
            () => new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
            });

        return cacheItem?.ToSnapshot()
               ?? new DataScopeSnapshot(false, false, Array.Empty<Guid>(), userId);
    }

    private string BuildCacheKey(long generation, Guid userId)
    {
        var tenantId = _currentUser.TenantId?.ToString() ?? "host";
        return $"datascope:v{DataScopeCacheItem.SchemaVersion}:{generation}:{tenantId}:{userId}";
    }

    private async Task<DataScopeSnapshot> ResolveAsync(Guid userId)
    {
        var roleNames = _currentUser.Roles;
        if (roleNames.Length == 0)
        {
            return new DataScopeSnapshot(false, false, Array.Empty<Guid>(), userId);
        }

        // 必须 WithDetailsAsync 显式加载 CustomOrganizationUnits，
        // 否则 GetListAsync 不加载导航属性，Custom 范围永远解析为空集合（fail-closed 零行可见）。
        var queryable = await _roleDataScopeRepository.WithDetailsAsync(x => x.CustomOrganizationUnits);
        var roleScopes = await _asyncExecuter.ToListAsync(
            queryable.Where(x => roleNames.Contains(x.RoleName)));

        if (roleScopes.Count == 0)
        {
            return new DataScopeSnapshot(false, false, Array.Empty<Guid>(), userId);
        }

        // 任一角色为 All 则整体 IsAll = true 短路返回
        if (roleScopes.Any(x => x.ScopeType == DataScopeTypeEnum.All))
        {
            return new DataScopeSnapshot(true, false, Array.Empty<Guid>(), userId);
        }

        var selfOnly = roleScopes.Any(x => x.ScopeType == DataScopeTypeEnum.SelfOnly);
        var ouIds = new HashSet<Guid>();

        // 收集 Custom 范围的组织
        foreach (var scope in roleScopes.Where(x => x.ScopeType == DataScopeTypeEnum.Custom))
        {
            foreach (var ou in scope.CustomOrganizationUnits)
            {
                ouIds.Add(ou.OrganizationUnitId);
            }
        }

        // 收集 CurrentOu / CurrentOuAndChildren 范围
        var needCurrentOu = roleScopes.Any(x =>
            x.ScopeType == DataScopeTypeEnum.CurrentOu ||
            x.ScopeType == DataScopeTypeEnum.CurrentOuAndChildren);

        if (needCurrentOu)
        {
            var userOus = await _userRepository.GetOrganizationUnitsAsync(userId);
            foreach (var ou in userOus)
            {
                ouIds.Add(ou.Id);
            }

            var needChildren = roleScopes.Any(x => x.ScopeType == DataScopeTypeEnum.CurrentOuAndChildren);
            if (needChildren)
            {
                // 多个 OU 的子树查询合并为一次 Code 前缀查询：ABP OU 的 Code 是物化路径，
                // GetAllChildrenWithParentCodeAsync 的语义就是 Code.StartsWith(parentCode)——
                // 用户挂 N 个 OU 时避免同请求 N 次子树查询
                var parentCodes = userOus
                    .Select(o => o.Code)
                    .Where(code => !string.IsNullOrEmpty(code))
                    .ToList();
                if (parentCodes.Count > 0)
                {
                    var userOuIds = userOus.Select(o => o.Id).ToList();
                    // IOrganizationUnitRepository 没有 Expression 谓词重载，走 Queryable 合并查询
                    var ouQueryable = await _organizationUnitQueryRepository.GetQueryableAsync();
                    var children = await _asyncExecuter.ToListAsync(
                        ouQueryable.Where(x =>
                            parentCodes.Any(code => x.Code.StartsWith(code)) && !userOuIds.Contains(x.Id)));
                    foreach (var child in children)
                    {
                        ouIds.Add(child.Id);
                    }
                }
            }
        }

        return new DataScopeSnapshot(false, selfOnly, ouIds.ToList(), userId);
    }
}
