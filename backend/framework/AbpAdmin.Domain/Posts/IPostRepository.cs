using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace AbpAdmin.Posts;

public interface IPostRepository : IRepository<Post, Guid>
{
    Task<Post?> FindByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<Post?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    Task<int> GetMemberCountAsync(Guid postId, CancellationToken cancellationToken = default);

    /// <summary>岗位成员分页（按用户名/姓名/邮箱关键字过滤）。</summary>
    Task<List<IdentityUser>> GetMembersAsync(
        Guid postId,
        string? filter = null,
        int skipCount = 0,
        int maxResultCount = int.MaxValue,
        CancellationToken cancellationToken = default);

    /// <summary>用户关联的岗位列表（按 SortOrder 排序）。</summary>
    Task<List<Post>> GetListByUserIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
