using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.DataScopes;

/* 验证 T1.5 行级数据权限的 5 种范围类型全局筛选器行为。
 * 测试策略：直接操作 ICurrentDataScopeState.Change 模拟不同用户的数据范围快照，
 * 然后查询 DataScopeDemo 验证全局筛选器是否正确过滤。
 *
 * 语义定死：
 * 1. CurrentOu = 用户所属全部 OU
 * 2. 可见组织集合为空 = 零行可见（fail-closed）
 * 3. OrganizationUnitId == null 的行只对 IsAll 可见
 * 4. SelfOnly 与机构范围是「或」关系
 * 5. 范围合并必须可交换、可结合
 * 6. 全局筛选器的覆写不得被复制到第二个 DbContext
 * 7. RoleDataScope、OrganizationUnit、IdentityUser 绝不能实现 IHasDataScope
 */
public abstract class DataScopeFilterTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IRepository<DataScopeDemo, Guid> _demoRepository;
    private readonly ICurrentDataScopeState _dataScopeState;
    private readonly IDataFilter _dataFilter;

    protected DataScopeFilterTests()
    {
        _demoRepository = GetRequiredService<IRepository<DataScopeDemo, Guid>>();
        _dataScopeState = GetRequiredService<ICurrentDataScopeState>();
        _dataFilter = GetRequiredService<IDataFilter>();
    }

    /// <summary>
    /// 准备测试数据：3 条记录，分别属于 OU-A、OU-B、无 OU（null）。
    /// </summary>
    private async Task<(Guid ouAId, Guid ouBId, Guid demoAId, Guid demoBId, Guid demoNullId)> SeedTestDataAsync()
    {
        var ouAId = Guid.NewGuid();
        var ouBId = Guid.NewGuid();
        var demoAId = Guid.NewGuid();
        var demoBId = Guid.NewGuid();
        var demoNullId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            // 禁用筛选器插入测试数据
            using (_dataFilter.Disable<IDataScopeEnabled>())
            {
                await _demoRepository.InsertAsync(new DataScopeDemo(demoAId, "Demo-A", ouAId));
                await _demoRepository.InsertAsync(new DataScopeDemo(demoBId, "Demo-B", ouBId));
                await _demoRepository.InsertAsync(new DataScopeDemo(demoNullId, "Demo-Null", null));
            }
        });

        return (ouAId, ouBId, demoAId, demoBId, demoNullId);
    }

    [Fact]
    public async Task Should_Return_All_When_IsAll()
    {
        var (ouAId, ouBId, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: true,
            selfOnly: false,
            organizationUnitIds: Array.Empty<Guid>(),
            userId: null)))
        {
            var list = await _demoRepository.GetListAsync();
            list.Count.ShouldBe(3);
            list.ShouldContain(x => x.Id == demoAId);
            list.ShouldContain(x => x.Id == demoBId);
            list.ShouldContain(x => x.Id == demoNullId);
        }
    }

    [Fact]
    public async Task Should_Return_Only_Specified_Ou_When_CurrentOu()
    {
        var (ouAId, ouBId, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();

        // 只可见 OU-A
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false,
            selfOnly: false,
            organizationUnitIds: new[] { ouAId },
            userId: null)))
        {
            var list = await _demoRepository.GetListAsync();
            list.Count.ShouldBe(1);
            list.ShouldContain(x => x.Id == demoAId);
            list.ShouldNotContain(x => x.Id == demoBId);
            list.ShouldNotContain(x => x.Id == demoNullId); // null OU 不可见
        }
    }

    [Fact]
    public async Task Should_Return_Only_Specified_Ou_When_Custom()
    {
        var (ouAId, ouBId, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();

        // 自定义可见 OU-B
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false,
            selfOnly: false,
            organizationUnitIds: new[] { ouBId },
            userId: null)))
        {
            var list = await _demoRepository.GetListAsync();
            list.Count.ShouldBe(1);
            list.ShouldContain(x => x.Id == demoBId);
            list.ShouldNotContain(x => x.Id == demoAId);
            list.ShouldNotContain(x => x.Id == demoNullId);
        }
    }

    [Fact]
    public async Task Should_Return_Multiple_Ous_When_Custom_With_Multiple()
    {
        var (ouAId, ouBId, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();

        // 自定义可见 OU-A 和 OU-B
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false,
            selfOnly: false,
            organizationUnitIds: new[] { ouAId, ouBId },
            userId: null)))
        {
            var list = await _demoRepository.GetListAsync();
            list.Count.ShouldBe(2);
            list.ShouldContain(x => x.Id == demoAId);
            list.ShouldContain(x => x.Id == demoBId);
            list.ShouldNotContain(x => x.Id == demoNullId);
        }
    }

    [Fact]
    public async Task Should_Return_Zero_When_Empty_Ou_List_FailClosed()
    {
        var (ouAId, ouBId, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();

        // 可见组织为空 = 零行可见（fail-closed）
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false,
            selfOnly: false,
            organizationUnitIds: Array.Empty<Guid>(),
            userId: null)))
        {
            var list = await _demoRepository.GetListAsync();
            list.Count.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Should_Return_Only_Self_Created_When_SelfOnly()
    {
        var (ouAId, ouBId, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();
        var userId = Guid.NewGuid();

        // SelfOnly：只看 CreatorId == userId 的记录
        // 但测试数据没有 CreatorId，所以应该返回 0 条
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false,
            selfOnly: true,
            organizationUnitIds: Array.Empty<Guid>(),
            userId: userId)))
        {
            var list = await _demoRepository.GetListAsync();
            list.Count.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Should_Return_Ou_Or_Self_When_SelfOnly_With_Ou()
    {
        var (ouAId, ouBId, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();
        var userId = Guid.NewGuid();

        // SelfOnly + OU-A：OU-A 的记录 + 自己创建的记录
        // 测试数据没有 CreatorId，所以只看 OU-A
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false,
            selfOnly: true,
            organizationUnitIds: new[] { ouAId },
            userId: userId)))
        {
            var list = await _demoRepository.GetListAsync();
            list.Count.ShouldBe(1);
            list.ShouldContain(x => x.Id == demoAId);
        }
    }

    [Fact]
    public async Task Should_Not_Leak_State_Between_Scopes()
    {
        var (ouAId, ouBId, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();

        // 第一个作用域：IsAll
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: true,
            selfOnly: false,
            organizationUnitIds: Array.Empty<Guid>(),
            userId: null)))
        {
            var list = await _demoRepository.GetListAsync();
            list.Count.ShouldBe(3);
        }

        // 第二个作用域：只看 OU-A
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false,
            selfOnly: false,
            organizationUnitIds: new[] { ouAId },
            userId: null)))
        {
            var list = await _demoRepository.GetListAsync();
            list.Count.ShouldBe(1);
        }

        // 第三个作用域：恢复默认（无状态）
        // 默认状态 IsAll=false, SelfOnly=false, OrganizationUnitIds=empty
        // 应该返回 0 条（fail-closed）
        var defaultList = await _demoRepository.GetListAsync();
        defaultList.Count.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Bypass_Filter_When_DataFilter_Disabled()
    {
        var (ouAId, ouBId, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();

        // 设置一个限制性快照
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false,
            selfOnly: false,
            organizationUnitIds: new[] { ouAId },
            userId: null)))
        {
            // 正常查询只能看到 OU-A
            var list = await _demoRepository.GetListAsync();
            list.Count.ShouldBe(1);

            // 禁用筛选器后能看到全部
            using (_dataFilter.Disable<IDataScopeEnabled>())
            {
                var allList = await _demoRepository.GetListAsync();
                allList.Count.ShouldBe(3);
            }
        }
    }
}
