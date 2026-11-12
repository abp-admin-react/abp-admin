using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

/// <summary>
/// 用户<strong>直接</strong>角色名查询（只读 AbpUserRoles，不含组织单元派生）。
/// 编辑种子、FilterRoles keep-set、Excel 导出共用，避免三处手写 join 漂移后再次把 OU 角色固化进 SetRolesAsync。
/// </summary>
public interface IUserDirectRoleNameLookup
{
    /// <summary>
    /// 单个用户的直接角色名；无直接角色时返回空列表（调用方若需区分「用户不存在」须先自行校验用户）。
    /// </summary>
    /// <param name="userId">用户 Id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyList<string>> GetAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量查询直接角色名；返回字典对每个入参 userId 都有键（无成员则为空列表）。
    /// </summary>
    /// <param name="userIds">用户 Id 列表。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetManyAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IUserDirectRoleNameLookup" />
public class UserDirectRoleNameLookup : IUserDirectRoleNameLookup, ITransientDependency
{
    /// <summary>与 OU 归属批量查询同口径，避免超长 IN 列表。</summary>
    private const int UserIdBatchSize = 1000;

    private readonly IRepository<IdentityUserRole> _userRoleRepository;
    private readonly IIdentityRoleRepository _roleRepository;

    public UserDirectRoleNameLookup(
        IRepository<IdentityUserRole> userRoleRepository,
        IIdentityRoleRepository roleRepository)
    {
        _userRoleRepository = userRoleRepository;
        _roleRepository = roleRepository;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var map = await GetManyAsync([userId], cancellationToken);
        return map[userId];
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<string>>> GetManyAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        if (userIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<string>>();
        }

        var roleIdsByUserId = new Dictionary<Guid, List<Guid>>();
        var allDirectRoleIds = new HashSet<Guid>();
        foreach (var userIdChunk in userIds.Chunk(UserIdBatchSize))
        {
            var chunkIds = userIdChunk.ToList();
            var roleLinks = await _userRoleRepository.GetListAsync(
                x => chunkIds.Contains(x.UserId),
                includeDetails: false,
                cancellationToken);
            foreach (var link in roleLinks)
            {
                if (!roleIdsByUserId.TryGetValue(link.UserId, out var roleIds))
                {
                    roleIdsByUserId[link.UserId] = roleIds = [];
                }

                roleIds.Add(link.RoleId);
                allDirectRoleIds.Add(link.RoleId);
            }
        }

        var roleNameById = allDirectRoleIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _roleRepository.GetListAsync(allDirectRoleIds.ToList(), cancellationToken: cancellationToken))
                .ToDictionary(role => role.Id, role => role.Name);

        return userIds.ToDictionary(
            id => id,
            id => (IReadOnlyList<string>)(roleIdsByUserId.GetValueOrDefault(id) ?? [])
                .Select(roleId => roleNameById.GetValueOrDefault(roleId))
                .Where(name => name != null)
                .Select(name => name!)
                .ToList());
    }
}
