using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Posts;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Posts;

/* 岗位管理（对标 RuoYi sys_post）测试。
 * 覆盖：CRUD、租户内名称/编码唯一、停用岗位不出现在 lookup、
 * 成员增删查、岗位删除连带清理关联、按用户查岗位。
 * 测试基建 AddAlwaysAllowAuthorization 使 [Authorize] 恒真，权限维度不在此断言。
 */
public abstract class PostAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IPostAppService _postAppService;
    private readonly IPostRepository _postRepository;
    private readonly IRepository<IdentityUserPost> _userPostRepository;
    private readonly IGuidGenerator _guidGenerator;

    protected PostAppServiceTests()
    {
        _postAppService = GetRequiredService<IPostAppService>();
        _postRepository = GetRequiredService<IPostRepository>();
        _userPostRepository = GetRequiredService<IRepository<IdentityUserPost>>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    private async Task<PostDto> CreatePostAsync(string? name = null, string? code = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return await _postAppService.CreateAsync(new CreatePostDto
        {
            Name = name ?? "岗位" + suffix,
            Code = code ?? "P" + suffix,
            SortOrder = 0,
            Status = PostStatusEnum.Enabled
        });
    }

    private async Task<IdentityUser> CreateUserAsync()
    {
        var userManager = GetRequiredService<IdentityUserManager>();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var user = new IdentityUser(
            _guidGenerator.Create(),
            "u" + suffix,
            "u" + suffix + "@test.abpadmin.local");
        (await userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();
        return user;
    }

    [Fact]
    public async Task Should_Create_And_List_Post()
    {
        var post = await CreatePostAsync("CEO岗", "CEO");

        post.Id.ShouldNotBe(Guid.Empty);
        post.Name.ShouldBe("CEO岗");
        post.Code.ShouldBe("CEO");

        var list = await _postAppService.GetListAsync(new GetPostListInput { Filter = "CEO" });
        list.Items.Any(x => x.Id == post.Id).ShouldBeTrue();
    }

    [Fact]
    public async Task Should_Reject_Duplicate_Name_And_Code()
    {
        await CreatePostAsync("重复岗位", "DUP");

        (await Should.ThrowAsync<BusinessException>(() =>
            CreatePostAsync("重复岗位", "OTHER")))
            .Code.ShouldBe(AbpAdminDomainErrorCodes.Posts.PostNameDuplicate);

        (await Should.ThrowAsync<BusinessException>(() =>
            CreatePostAsync("另一个岗位", "DUP")))
            .Code.ShouldBe(AbpAdminDomainErrorCodes.Posts.PostCodeDuplicate);
    }

    [Fact]
    public async Task Should_Allow_Update_Without_Self_Conflict()
    {
        var post = await CreatePostAsync("可改名岗位", "UPD");

        // 改排序和备注，名称编码不动 → 不应触发唯一冲突
        var updated = await _postAppService.UpdateAsync(post.Id, new UpdatePostDto
        {
            Name = post.Name,
            Code = post.Code,
            SortOrder = 9,
            Status = PostStatusEnum.Disabled,
            Remark = "备注"
        });
        updated.SortOrder.ShouldBe(9);
        updated.Status.ShouldBe(PostStatusEnum.Disabled);
        updated.Remark.ShouldBe("备注");
    }

    [Fact]
    public async Task Lookup_Should_Exclude_Disabled_Post()
    {
        var enabled = await CreatePostAsync();
        var disabled = await CreatePostAsync();
        await _postAppService.UpdateAsync(disabled.Id, new UpdatePostDto
        {
            Name = disabled.Name,
            Code = disabled.Code,
            SortOrder = 0,
            Status = PostStatusEnum.Disabled
        });

        var lookup = await _postAppService.GetLookupAsync();
        lookup.Items.Any(x => x.Id == enabled.Id).ShouldBeTrue();
        lookup.Items.Any(x => x.Id == disabled.Id).ShouldBeFalse();
    }

    [Fact]
    public async Task Should_Manage_Members()
    {
        var post = await CreatePostAsync();
        var user1 = await CreateUserAsync();
        var user2 = await CreateUserAsync();

        await _postAppService.AddMembersAsync(post.Id, new[] { user1.Id, user2.Id, user1.Id });

        var members = await _postAppService.GetMembersAsync(post.Id, new GetPostMembersInput());
        members.TotalCount.ShouldBe(2);
        members.Items.Select(x => x.Id).ShouldContain(user1.Id);

        var userPosts = await _postAppService.GetPostsByUserAsync(user1.Id);
        userPosts.Items.Any(x => x.Id == post.Id).ShouldBeTrue();

        // 列表的成员数聚合
        var list = await _postAppService.GetListAsync(new GetPostListInput { Filter = post.Name });
        list.Items.Single(x => x.Id == post.Id).MemberCount.ShouldBe(2);

        await _postAppService.RemoveMemberAsync(post.Id, user1.Id);
        var afterRemove = await _postAppService.GetMembersAsync(post.Id, new GetPostMembersInput());
        afterRemove.TotalCount.ShouldBe(1);

        // 重复添加幂等
        await _postAppService.AddMembersAsync(post.Id, new[] { user2.Id });
        (await _postAppService.GetMembersAsync(post.Id, new GetPostMembersInput()))
            .TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task Delete_Post_Should_Clear_Associations()
    {
        var post = await CreatePostAsync();
        var user = await CreateUserAsync();
        await _postAppService.AddMembersAsync(post.Id, new[] { user.Id });

        await _postAppService.DeleteAsync(post.Id);

        (await _postAppService.GetListAsync(new GetPostListInput { Filter = post.Name }))
            .Items.Any(x => x.Id == post.Id).ShouldBeFalse();

        await WithUnitOfWorkAsync(async () =>
        {
            var remaining = await _userPostRepository.GetListAsync(x => x.PostId == post.Id);
            remaining.ShouldBeEmpty();
        });

        // 删除后同名同编码可重建（硬删已释放唯一索引）。
        // SortOrder 故意与第一次不同：CreateAsync 挂了 [PreventDuplicateSubmit]，
        // 参数指纹含全部字段，完全相同的入参会被窗口标记拦下（那是防重测试的职责）。
        var recreated = await _postAppService.CreateAsync(new CreatePostDto
        {
            Name = post.Name,
            Code = post.Code,
            SortOrder = 1,
            Status = PostStatusEnum.Enabled
        });
        recreated.Id.ShouldNotBe(post.Id);
    }
}
