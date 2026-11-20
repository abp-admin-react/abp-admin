using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Grids;
using AbpAdmin.Linq.DynamicQueryable;
using NSubstitute;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.MultiTenancy;
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

    [Fact]
    public async Task Grid_should_filter_and_sort_all_users_before_paging()
    {
        var prefix = $"grid_{_guidGenerator.Create():N}";
        await CreateUserAsync(prefix + "_alice");
        var betty = await CreateUserAsync(prefix + "_betty");
        var carl = await CreateUserAsync(prefix + "_carl");
        (await _userManager.SetLockoutEndDateAsync(betty, DateTimeOffset.UtcNow.AddDays(1))).Succeeded.ShouldBeTrue();
        (await _userManager.SetTwoFactorEnabledAsync(carl, true)).Succeeded.ShouldBeTrue();

        var query = new GridQueryInput { Keyword = prefix, MaxResultCount = 1 };
        (await _searchAppService.QueryAsync(query)).Items.Single().UserName.ShouldBe(prefix + "_alice");
        query.Filters =
        [
            new() { Field = "status", Join = "or", Conditions = [
                new() { Op = "eq", Value = "active" }, new() { Op = "eq", Value = "locked" }] },
            new() { Field = "userName", Conditions = [new() { Op = "notContains", Value = "alice" }] }
        ];
        query.Sort = [new() { Field = "twoFactorEnabled", Direction = "desc" }, new() { Field = "userName" }];
        var first = await _searchAppService.QueryAsync(query);
        first.TotalCount.ShouldBe(2);
        first.Items.Single().Id.ShouldBe(carl.Id);
        first.Items.Single().TwoFactorEnabled.ShouldBe(true);
        query.SkipCount = 1;
        var second = await _searchAppService.QueryAsync(query);
        second.Items.Single().Status.ShouldBe("locked");
        second.Items.Single().IsLocked.ShouldBeTrue();
        second.Items.Single().ConcurrencyStamp.ShouldBe(betty.ConcurrencyStamp);
        using (GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
        {
            (await _searchAppService.QueryAsync(query)).TotalCount.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Grid_should_restrict_contact_and_two_factor_fields_for_read_only_accounts()
    {
        var user = await CreateUserAsync($"private_{_guidGenerator.Create():N}");
        await WithUnitOfWorkAsync(async () =>
        {
            var readOnly = new IdentityUserSearchAppService(GetRequiredService<IRepository<IdentityUser, Guid>>(),
                Substitute.For<IPermissionChecker>())
            {
                LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>()
            };
            var input = new GridQueryInput { Keyword = user.Email };
            (await readOnly.QueryAsync(input)).TotalCount.ShouldBe(0);
            (await _searchAppService.QueryAsync(input)).Items.Single().Id.ShouldBe(user.Id);
            input.Keyword = user.UserName;
            (await readOnly.QueryAsync(input)).Items.Single().TwoFactorEnabled.ShouldBeNull();

            foreach (var field in new[] { "email", "phoneNumber", "twoFactorEnabled" })
            {
                var filter = new GridQueryInput { Filters = [new() { Field = field, Conditions = [new() { Op = "eq", Value = "true" }] }] };
                await Should.ThrowAsync<AbpValidationException>(() => readOnly.QueryAsync(filter));
                var sort = new GridQueryInput { Sort = [new() { Field = field }] };
                await Should.ThrowAsync<AbpValidationException>(() => readOnly.QueryAsync(sort));
            }
        });
    }

    private async Task<IdentityUser> CreateUserAsync(string userName)
    {
        var user = new IdentityUser(_guidGenerator.Create(), userName, $"{userName}@abpadmin.test");
        (await _userManager.CreateAsync(user)).Succeeded.ShouldBeTrue();
        return user;
    }
}
