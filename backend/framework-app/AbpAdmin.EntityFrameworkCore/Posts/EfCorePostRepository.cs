using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;

namespace AbpAdmin.Posts;

public class EfCorePostRepository : EfCoreRepository<AbpAdminDbContext, Post, Guid>, IPostRepository
{
    public EfCorePostRepository(IDbContextProvider<AbpAdminDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<Post?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.FirstOrDefaultAsync(x => x.Code == code, cancellationToken);
    }

    public virtual async Task<Post?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.FirstOrDefaultAsync(x => x.Name == name, cancellationToken);
    }

    public virtual async Task<int> GetMemberCountAsync(Guid postId, CancellationToken cancellationToken = default)
    {
        var userPostSet = (await GetDbContextAsync()).Set<IdentityUserPost>();
        return await userPostSet.CountAsync(x => x.PostId == postId, cancellationToken);
    }

    public virtual async Task<List<IdentityUser>> GetMembersAsync(
        Guid postId,
        string? filter = null,
        int skipCount = 0,
        int maxResultCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var userPostSet = dbContext.Set<IdentityUserPost>();
        var userSet = dbContext.Set<IdentityUser>();

        var query =
            from userPost in userPostSet
            join user in userSet on userPost.UserId equals user.Id
            where userPost.PostId == postId
            select user;

        if (!string.IsNullOrWhiteSpace(filter))
        {
            query = query.Where(u =>
                u.UserName.Contains(filter) ||
                (u.Email != null && u.Email.Contains(filter)) ||
                (u.Name != null && u.Name.Contains(filter)));
        }

        return await query
            .OrderBy(u => u.UserName)
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync(cancellationToken);
    }

    public virtual async Task<List<Post>> GetListByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var dbContext = await GetDbContextAsync();
        var postSet = dbContext.Set<Post>();
        var userPostSet = dbContext.Set<IdentityUserPost>();

        var query =
            from userPost in userPostSet
            join post in postSet on userPost.PostId equals post.Id
            where userPost.UserId == userId
            select post;

        return await query
            .OrderBy(p => p.SortOrder)
            .ThenBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }
}
