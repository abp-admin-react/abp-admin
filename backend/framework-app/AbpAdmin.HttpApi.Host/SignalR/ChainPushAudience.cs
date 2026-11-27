using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SimpleStateChecking;
using Volo.Abp.Uow;

namespace AbpAdmin.SignalR;

public interface IChainPushAudience
{
    Task<IReadOnlyList<string>> GetGroupsAsync(string[] permissions);
}

/// <summary>每个资金事件按查询收件人时已提交的宿主授权投递。撤权从提交后的下一次收件人查询生效。</summary>
public class ChainPushAudience(
    IDbContextProvider<AbpAdminDbContext> databases,
    IUnitOfWorkManager units,
    ICurrentTenant tenant,
    IPermissionDefinitionManager definitions,
    ISimpleStateCheckerManager<PermissionDefinition> state) : IChainPushAudience, ITransientDependency
{
    /// <summary>合并用户、角色（含组织角色）和客户端授予的候选权限，每个连接最多命中一个接收组。</summary>
    public async Task<IReadOnlyList<string>> GetGroupsAsync(string[] permissions)
    {
        using var host = tenant.Change(null);
        using var read = units.Begin(requiresNew: true, isTransactional: false);
        var eligible = new List<PermissionDefinition>(permissions.Length);
        foreach (var name in permissions)
        {
            var definition = await definitions.GetOrNullAsync(name);
            if (definition != null && definition.IsEnabled && definition.MultiTenancySide.HasFlag(MultiTenancySides.Host)
                && await state.IsEnabledAsync(definition))
                eligible.Add(definition);
        }
        if (eligible.Count == 0) return [];

        string[] For(string provider) => eligible.Where(d => d.Providers.Count == 0 || d.Providers.Contains(provider))
            .Select(d => d.Name).ToArray();
        var userPermissions = For(UserPermissionValueProvider.ProviderName);
        var rolePermissions = For(RolePermissionValueProvider.ProviderName);
        var clientPermissions = For(ClientPermissionValueProvider.ProviderName);
        var db = await databases.GetDbContextAsync();
        var grants = db.Set<PermissionGrant>().Where(g => g.TenantId == null);
        var users = db.Users.Where(u => u.TenantId == null && !u.IsDeleted && u.IsActive);
        var userKeys = grants.Where(g => g.ProviderName == UserPermissionValueProvider.ProviderName
            && userPermissions.Contains(g.Name)).Select(g => g.ProviderKey);
        var roleKeys = grants.Where(g => g.ProviderName == RolePermissionValueProvider.ProviderName
            && rolePermissions.Contains(g.Name)).Select(g => g.ProviderKey);
        var roles = db.Roles.Where(r => r.TenantId == null && roleKeys.Contains(r.Name)).Select(r => r.Id);
        var directMembers = db.Set<IdentityUserRole>().Where(r => r.TenantId == null && roles.Contains(r.RoleId))
            .Select(r => r.UserId);
        // 与 IdentityUserRepository.GetRoleNamesAsync 相同：用户所属组织直接授予的角色也生效。
        var organizationMembers =
            from membership in db.Set<IdentityUserOrganizationUnit>()
            join unit in db.OrganizationUnits on membership.OrganizationUnitId equals unit.Id
            join binding in db.Set<OrganizationUnitRole>() on unit.Id equals binding.OrganizationUnitId
            where membership.TenantId == null && unit.TenantId == null && !unit.IsDeleted && binding.TenantId == null
                && roles.Contains(binding.RoleId)
            select membership.UserId;
        var personal = users.Where(u => userKeys.Contains(u.Id.ToString()) || directMembers.Contains(u.Id)
            || organizationMembers.Contains(u.Id)).Select(u => u.Id);
        var clients = grants.Where(g => g.ProviderName == ClientPermissionValueProvider.ProviderName
            && clientPermissions.Contains(g.Name)).Select(g => g.ProviderKey).Distinct();

        // 用户或角色授权适用于该用户所有已准入连接；仅客户端授权必须进一步匹配连接上已认证的 client_id。
        // 两组由同一 SQL 快照求差集，避免发送重复以及两次查库之间的撤权竞争。
        var addresses = await personal.Select(userId => new { UserId = userId, Client = (string?)null })
            .Concat(from client in clients
                    from user in users
                    where !personal.Contains(user.Id)
                    select new { UserId = user.Id, Client = (string?)client })
            .ToListAsync();
        await read.CompleteAsync();
        return addresses.Select(a => ChainHubGroups.ForRecipient(a.UserId, a.Client)).ToArray();
    }
}
