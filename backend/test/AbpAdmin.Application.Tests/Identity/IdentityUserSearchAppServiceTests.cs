using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Linq.DynamicQueryable;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.Validation;
using Xunit;

namespace AbpAdmin.Identity;

/* 用户动态搜索（dynamic-queryable 落地）应用层测试。
 * 核心语义：条件翻译正确（文本/布尔/字段白名单/排序白名单）。
 * 用户名带唯一标记，测试库在 collection 内共享。
 */
public abstract class IdentityUserSearchAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IIdentityUserSearchAppService _searchAppService;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IdentityUserManager _userManager;

    protected IdentityUserSearchAppServiceTests()
    {
        _searchAppService = GetRequiredService<IIdentityUserSearchAppService>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
        _userManager = GetRequiredService<IdentityUserManager>();
    }

    [Fact]
    public async Task GetAvailableFields_Should_Return_Whitelist_With_Metadata()
    {
        var fields = await _searchAppService.GetAvailableFieldsAsync();

        fields.ShouldNotBeEmpty();
        fields.Select(f => f.Field).ShouldContain("UserName");
        fields.Select(f => f.Field).ShouldContain("CreationTime");
        var userName = fields.Single(f => f.Field == "UserName");
        userName.ValueType.ShouldBe("string");
        userName.Comparisons.ShouldContain(DynamicComparison.Contains);
    }

    [Fact]
    public async Task Search_Should_Filter_By_Contains()
    {
        var marker = $"dynsearch_{_guidGenerator.Create():N}";
        await CreateUserAsync($"{marker}_alice");
        await CreateUserAsync($"{marker}_bob");

        var result = await _searchAppService.SearchAsync(new IdentityUserDynamicSearchInput
        {
            Conditions = new()
            {
                new DynamicCondition { Field = "UserName", Comparison = DynamicComparison.Contains, Value = marker },
            },
            MaxResultCount = 50,
        });

        result.TotalCount.ShouldBe(2);
        result.Items.All(u => u.UserName.Contains(marker)).ShouldBeTrue();
    }

    [Fact]
    public async Task Search_Should_Filter_By_Boolean_Equality()
    {
        var marker = $"dynsearch_{_guidGenerator.Create():N}";
        await CreateUserAsync($"{marker}_alice");

        // 新建用户默认 IsActive=true，与 marker 组合应恰好命中
        var result = await _searchAppService.SearchAsync(new IdentityUserDynamicSearchInput
        {
            Conditions = new()
            {
                new DynamicCondition { Field = "UserName", Comparison = DynamicComparison.Contains, Value = marker },
                new DynamicCondition { Field = "IsActive", Comparison = DynamicComparison.Equal, Value = "true" },
            },
            MaxResultCount = 50,
        });

        result.TotalCount.ShouldBe(1);
        result.Items[0].UserName.ShouldBe($"{marker}_alice");
    }

    [Fact]
    public async Task Search_Should_Combine_Conditions_With_Or()
    {
        var marker = $"dynsearch_{_guidGenerator.Create():N}";
        await CreateUserAsync($"{marker}_alice");
        await CreateUserAsync($"{marker}_bob");
        await CreateUserAsync($"{marker}_carol");

        var result = await _searchAppService.SearchAsync(new IdentityUserDynamicSearchInput
        {
            Conditions = new()
            {
                new DynamicCondition { Field = "UserName", Comparison = DynamicComparison.EndsWith, Value = "_alice" },
                new DynamicCondition { Field = "UserName", Comparison = DynamicComparison.EndsWith, Value = "_bob", Logic = DynamicLogic.Or },
            },
            MaxResultCount = 50,
        });

        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Search_Should_Reject_Too_Many_Conditions()
    {
        var conditions = Enumerable.Range(0, 33)
            .Select(i => new DynamicCondition { Field = "UserName", Comparison = DynamicComparison.Contains, Value = i.ToString() })
            .ToList();

        var exception = await Should.ThrowAsync<AbpValidationException>(() =>
            _searchAppService.SearchAsync(new IdentityUserDynamicSearchInput { Conditions = conditions }));

        exception.Message.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task Search_Should_Reject_Field_Outside_Whitelist()
    {
        var exception = await Should.ThrowAsync<UserFriendlyException>(() =>
            _searchAppService.SearchAsync(new IdentityUserDynamicSearchInput
            {
                Conditions = new()
                {
                    new DynamicCondition { Field = "PasswordHash", Comparison = DynamicComparison.Equal, Value = "x" },
                },
            }));

        exception.Message.ShouldContain("PasswordHash");
    }

    [Fact]
    public async Task Search_Should_Reject_Invalid_Sorting()
    {
        await Should.ThrowAsync<AbpValidationException>(() =>
            _searchAppService.SearchAsync(new IdentityUserDynamicSearchInput
            {
                Sorting = "PasswordHash ASC",
            }));
    }

    private async Task<IdentityUser> CreateUserAsync(string userName)
    {
        var user = new IdentityUser(_guidGenerator.Create(), userName, $"{userName}@abpadmin.test");
        (await _userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();
        return user;
    }
}
