using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using AbpAdmin.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using AbpAdmin.OperationLogs;

namespace AbpAdmin.Posts;

[Authorize(AbpAdminPermissions.Posts.Default)]
public class PostAppService : AbpAdminAppService, IPostAppService
{
    private readonly IPostRepository _postRepository;
    private readonly IRepository<IdentityUserPost> _userPostRepository;
    private readonly IIdentityUserRepository _userRepository;

    public PostAppService(
        IPostRepository postRepository,
        IRepository<IdentityUserPost> userPostRepository,
        IIdentityUserRepository userRepository)
    {
        _postRepository = postRepository;
        _userPostRepository = userPostRepository;
        _userRepository = userRepository;
    }

    public virtual async Task<PagedResultDto<PostDto>> GetListAsync(GetPostListInput input)
    {
        var queryable = await _postRepository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter;
            queryable = queryable.Where(x =>
                x.Name.Contains(filter) || x.Code.Contains(filter));
        }

        if (input.Status.HasValue)
        {
            queryable = queryable.Where(x => x.Status == input.Status.Value);
        }

        queryable = queryable.OrderBy(nameof(Post.SortOrder), nameof(Post.Name));

        var totalCount = await AsyncExecuter.CountAsync(queryable);
        var posts = await AsyncExecuter.ToListAsync(
            queryable.Skip(input.SkipCount).Take(input.MaxResultCount));

        var postIds = posts.Select(x => x.Id).ToList();
        var userPostQueryable = await _userPostRepository.GetQueryableAsync();
        var counts = await AsyncExecuter.ToListAsync(
            userPostQueryable.Where(x => postIds.Contains(x.PostId))
                .GroupBy(x => x.PostId)
                .Select(x => new { PostId = x.Key, Count = x.Count() }));

        var countMap = counts.ToDictionary(x => x.PostId, x => x.Count);

        return new PagedResultDto<PostDto>(
            totalCount,
            posts.Select(post => Map(post, countMap.GetValueOrDefault(post.Id))).ToList());
    }

    public virtual async Task<ListResultDto<PostDto>> GetLookupAsync()
    {
        var posts = await _postRepository.GetListAsync(x => x.Status == PostStatusEnum.Enabled);
        return new ListResultDto<PostDto>(
            posts.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).Select(x => Map(x)).ToList());
    }

    [Authorize(AbpAdminPermissions.Posts.Create)]
    [PreventDuplicateSubmit] // 防重复提交参照实现：同用户同参数 5 秒窗口，成功占窗、失败即释放可重试
    [OperationLog("岗位管理", "创建岗位", BizNo = "{{_ret.id}}", Success = "创建了岗位「{{input.name}}」（{{input.code}}）")]
    public virtual async Task<PostDto> CreateAsync(CreatePostDto input)
    {
        await CheckNameUniqueAsync(input.Name, excludeId: null);
        await CheckCodeUniqueAsync(input.Code, excludeId: null);

        var post = new Post(
            GuidGenerator.Create(),
            input.Name,
            input.Code,
            input.SortOrder,
            input.Status,
            input.Remark,
            CurrentTenant.Id);

        await _postRepository.InsertAsync(post);
        return Map(post);
    }

    [Authorize(AbpAdminPermissions.Posts.Update)]
    [OperationLog("岗位管理", "更新岗位", BizNo = "{{id}}",
        Success = "更新了岗位「{{input.name}}」：{{_diff(old,input)}}")]
    public virtual async Task<PostDto> UpdateAsync(Guid id, UpdatePostDto input)
    {
        var post = await _postRepository.GetAsync(id);

        // 旧值快照供操作日志差异对比（GetAsync 返回被跟踪实体，必须先拷值再改）
        OperationLogScope.Set("old", new { post.Name, post.Code, post.SortOrder, post.Status, post.Remark });

        await CheckNameUniqueAsync(input.Name, excludeId: id);
        await CheckCodeUniqueAsync(input.Code, excludeId: id);

        post.SetName(input.Name);
        post.SetCode(input.Code);
        post.SetSortOrder(input.SortOrder);
        post.SetStatus(input.Status);
        post.SetRemark(input.Remark);

        await _postRepository.UpdateAsync(post);
        return Map(post);
    }

    [Authorize(AbpAdminPermissions.Posts.Delete)]
    [OperationLog("岗位管理", "删除岗位", BizNo = "{{id}}", Success = "删除了岗位「{{post(id)}}」")]
    public virtual async Task DeleteAsync(Guid id)
    {
        var post = await _postRepository.GetAsync(id);

        // 关联行硬删；岗位本体也硬删——软删除行仍占用 (TenantId, Name/Code) 唯一索引，
        // 重建同编码岗位会撞唯一约束（与菜单删除的 HardDelete 同理）
        await _userPostRepository.DeleteAsync(x => x.PostId == id);
        await _postRepository.HardDeleteAsync(post);
    }

    [Authorize(AbpAdminPermissions.Posts.Default)]
    public virtual async Task<PagedResultDto<PostUserDto>> GetMembersAsync(Guid id, GetPostMembersInput input)
    {
        await _postRepository.GetAsync(id);

        var users = await _postRepository.GetMembersAsync(
            id,
            input.Filter,
            input.SkipCount,
            input.MaxResultCount);

        var userPostQueryable = await _userPostRepository.GetQueryableAsync();
        var totalCount = await AsyncExecuter.CountAsync(
            userPostQueryable.Where(x => x.PostId == id));

        return new PagedResultDto<PostUserDto>(
            totalCount,
            users.Select(user => new PostUserDto
            {
                Id = user.Id,
                UserName = user.UserName,
                Name = user.Name,
                Email = user.Email,
                IsActive = user.IsActive
            }).ToList());
    }

    [Authorize(AbpAdminPermissions.Posts.ManageMembers)]
    public virtual async Task AddMembersAsync(Guid id, Guid[] userIds)
    {
        await _postRepository.GetAsync(id);

        var existingUserIds = (await _userPostRepository.GetListAsync(x => x.PostId == id))
            .Select(x => x.UserId)
            .ToHashSet();

        var idsToAdd = userIds.Distinct().Where(userId => !existingUserIds.Contains(userId)).ToList();
        if (idsToAdd.Count == 0)
        {
            return;
        }

        // 批量校验（一次查询代替 N 次 GetByIdAsync）；缺失的一并报出而不是只报第一个
        var foundUsers = await _userRepository.GetListByIdsAsync(idsToAdd);
        var missingIds = idsToAdd.Except(foundUsers.Select(u => u.Id)).ToList();
        if (missingIds.Count > 0)
        {
            throw new EntityNotFoundException(typeof(IdentityUser), string.Join(',', missingIds));
        }

        var tenantId = CurrentTenant.Id;
        await _userPostRepository.InsertManyAsync(
            idsToAdd.Select(userId => new IdentityUserPost(userId, id, tenantId)));
    }

    [Authorize(AbpAdminPermissions.Posts.ManageMembers)]
    public virtual async Task RemoveMemberAsync(Guid id, Guid userId)
    {
        await _userPostRepository.DeleteAsync(x => x.UserId == userId && x.PostId == id);
    }

    [Authorize(AbpAdminPermissions.Posts.Default)]
    public virtual async Task<ListResultDto<PostDto>> GetPostsByUserAsync(Guid userId)
    {
        await _userRepository.GetAsync(userId); // 用户不存在时抛 EntityNotFoundException

        var posts = await _postRepository.GetListByUserIdAsync(userId);
        return new ListResultDto<PostDto>(posts.Select(x => Map(x)).ToList());
    }

    protected virtual async Task CheckNameUniqueAsync(string name, Guid? excludeId)
    {
        var existing = await _postRepository.FindByNameAsync(name);
        if (existing != null && existing.Id != excludeId)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Posts.PostNameDuplicate)
                .WithData("Name", name);
        }
    }

    protected virtual async Task CheckCodeUniqueAsync(string code, Guid? excludeId)
    {
        var existing = await _postRepository.FindByCodeAsync(code);
        if (existing != null && existing.Id != excludeId)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Posts.PostCodeDuplicate)
                .WithData("Code", code);
        }
    }

    private static PostDto Map(Post post, int memberCount = 0)
    {
        return new PostDto
        {
            Id = post.Id,
            Name = post.Name,
            Code = post.Code,
            SortOrder = post.SortOrder,
            Status = post.Status,
            Remark = post.Remark,
            MemberCount = memberCount
        };
    }
}
