using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Entities;
using AbpAdmin.Biz.Template.Services;
using AbpAdmin.Biz.Template.Services.Dtos;
using AbpAdmin.DataScopes;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Security.Claims;
using Xunit;

namespace AbpAdmin.Biz.Template.DataScopes;

/* 数据权限示例（模块侧落地）的过滤契约：镜像框架 DataScopeFilterTests 的模拟手法——
 * ICurrentDataScopeState.Change 注入数据范围快照，验证模块 BizTemplateDbContext 里的
 * 数据范围全局筛选器与写入侧自动填充。本模块是筛选器代码的复制体（非继承），
 * 这里同时是它的 parity 防线——框架语义变更时两侧测试须同提交同步。
 * 语义与框架逐条对齐：
 * 1. IsAll 看全部（含 null OU 行）
 * 2. 指定 OU 只看对应行，null OU 行不可见（fail-closed）
 * 3. 可见组织集合为空 = 零行可见
 * 4. SelfOnly 与机构范围是「或」关系（两侧分支都正命中）
 * 5. IDataFilter.Disable<IDataScopeEnabled> 旁路（种子/后台作业姿势）
 * 6. AppService 查询同样吃到过滤（业务面零过滤代码）
 * 7. 写入侧：快照可算出组织 → 自动填充；显式赋值不覆盖；算不出 → 抛业务异常
 * 8. 写入侧防线（比框架演示夹具严）：显式组织必须在当前范围内（All 校验存在性）
 * 9. 多租户筛选器与 OU 筛选器组合后仍按（租户 ∩ OU）正确过滤
 */
public class BizDataScopeDemoFilterTests : BizTemplateTestBase
{
    private readonly IRepository<BizDataScopeDemo, Guid> _repository;
    private readonly IBizDataScopeDemoAppService _appService;
    private readonly ICurrentDataScopeState _dataScopeState;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;
    private readonly ICurrentPrincipalAccessor _currentPrincipalAccessor;

    public BizDataScopeDemoFilterTests()
    {
        _repository = GetRequiredService<IRepository<BizDataScopeDemo, Guid>>();
        _appService = GetRequiredService<IBizDataScopeDemoAppService>();
        _dataScopeState = GetRequiredService<ICurrentDataScopeState>();
        _dataFilter = GetRequiredService<IDataFilter>();
        _currentTenant = GetRequiredService<ICurrentTenant>();
        _currentPrincipalAccessor = GetRequiredService<ICurrentPrincipalAccessor>();
    }

    /// <summary>准备测试数据：3 条记录，分别属于 OU-A、OU-B、无 OU（null）。</summary>
    private async Task<(Guid ouAId, Guid ouBId, Guid demoAId, Guid demoBId, Guid demoNullId)> SeedTestDataAsync()
    {
        var ouAId = Guid.NewGuid();
        var ouBId = Guid.NewGuid();
        var demoAId = Guid.NewGuid();
        var demoBId = Guid.NewGuid();
        var demoNullId = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            // 禁用筛选器插入测试数据（种子姿势）
            using (_dataFilter.Disable<IDataScopeEnabled>())
            {
                await _repository.InsertAsync(new BizDataScopeDemo(demoAId, "Biz-Demo-A", ouAId));
                await _repository.InsertAsync(new BizDataScopeDemo(demoBId, "Biz-Demo-B", ouBId));
                await _repository.InsertAsync(new BizDataScopeDemo(demoNullId, "Biz-Demo-Null", null));
            }
        });

        return (ouAId, ouBId, demoAId, demoBId, demoNullId);
    }

    [Fact]
    public async Task Should_Return_All_When_IsAll()
    {
        var (_, _, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: true, selfOnly: false, organizationUnitIds: Array.Empty<Guid>(), userId: null)))
        {
            var list = await _repository.GetListAsync();
            list.Count.ShouldBe(3);
            list.ShouldContain(x => x.Id == demoAId);
            list.ShouldContain(x => x.Id == demoBId);
            list.ShouldContain(x => x.Id == demoNullId);
        }
    }

    [Fact]
    public async Task Should_Return_Only_Specified_Ou_When_CurrentOu()
    {
        var (ouAId, _, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: new[] { ouAId }, userId: null)))
        {
            var list = await _repository.GetListAsync();
            list.Count.ShouldBe(1);
            list.ShouldContain(x => x.Id == demoAId);
            list.ShouldNotContain(x => x.Id == demoBId);
            list.ShouldNotContain(x => x.Id == demoNullId); // null OU 不可见（fail-closed）
        }
    }

    [Fact]
    public async Task Should_Return_Multiple_Ous_When_Custom_With_Multiple()
    {
        var (ouAId, ouBId, demoAId, demoBId, demoNullId) = await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: new[] { ouAId, ouBId }, userId: null)))
        {
            var list = await _repository.GetListAsync();
            list.Count.ShouldBe(2); // Contains 多组织并集
            list.ShouldContain(x => x.Id == demoAId);
            list.ShouldContain(x => x.Id == demoBId);
            list.ShouldNotContain(x => x.Id == demoNullId);
        }
    }

    [Fact]
    public async Task Should_Return_Zero_When_Empty_Ou_List_FailClosed()
    {
        await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: Array.Empty<Guid>(), userId: null)))
        {
            var list = await _repository.GetListAsync();
            list.Count.ShouldBe(0);
        }
    }

    [Fact]
    public async Task Should_Return_Only_Self_Created_When_SelfOnly()
    {
        // 两个不同「当前用户」各插一条（CreatorId 由 ABP 审计从当前主体回填），
        // SelfOnly 只看 CreatorId == userId 的记录——CreatorId 分支的正命中
        var userAId = Guid.NewGuid();
        var userBId = Guid.NewGuid();
        var demoOfA = Guid.NewGuid();
        var demoOfB = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<IDataScopeEnabled>())
            {
                using (_currentPrincipalAccessor.Change(CreatePrincipal(userAId)))
                {
                    await _repository.InsertAsync(new BizDataScopeDemo(demoOfA, "Biz-Demo-Of-A", null));
                }

                using (_currentPrincipalAccessor.Change(CreatePrincipal(userBId)))
                {
                    await _repository.InsertAsync(new BizDataScopeDemo(demoOfB, "Biz-Demo-Of-B", null));
                }
            }
        });

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: true, organizationUnitIds: Array.Empty<Guid>(), userId: userAId)))
        {
            var list = await _repository.GetListAsync();
            list.Count.ShouldBe(1);
            list.ShouldContain(x => x.Id == demoOfA);
        }
    }

    [Fact]
    public async Task Should_Return_Ou_Or_Self_When_SelfOnly_With_Ou()
    {
        var (ouAId, _, demoAId, _, _) = await SeedTestDataAsync();
        var userId = Guid.NewGuid();

        // SelfOnly + OU-A：OU-A 的记录 + 自己创建的记录
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: true, organizationUnitIds: new[] { ouAId }, userId: userId)))
        {
            var list = await _repository.GetListAsync();
            list.Count.ShouldBe(1);
            list.ShouldContain(x => x.Id == demoAId);
        }
    }

    [Fact]
    public async Task Should_Not_Leak_State_Between_Scopes()
    {
        var (ouAId, _, _, _, _) = await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: true, selfOnly: false, organizationUnitIds: Array.Empty<Guid>(), userId: null)))
        {
            (await _repository.GetListAsync()).Count.ShouldBe(3);
        }

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: new[] { ouAId }, userId: null)))
        {
            (await _repository.GetListAsync()).Count.ShouldBe(1);
        }

        // 无活动快照的默认态（中间件未注入）：fail-closed 零行可见，作用域间不泄漏
        (await _repository.GetListAsync()).Count.ShouldBe(0);
    }

    [Fact]
    public async Task Should_Bypass_Filter_When_DataFilter_Disabled()
    {
        var (ouAId, _, _, _, _) = await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: new[] { ouAId }, userId: null)))
        {
            var list = await _repository.GetListAsync();
            list.Count.ShouldBe(1);

            using (_dataFilter.Disable<IDataScopeEnabled>())
            {
                var allList = await _repository.GetListAsync();
                allList.Count.ShouldBe(3);
            }
        }
    }

    [Fact]
    public async Task Should_Filter_Through_AppService_List()
    {
        var (ouAId, _, _, _, _) = await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: new[] { ouAId }, userId: null)))
        {
            // AppService 里没有任何过滤代码——可见性完全来自模块 DbContext 的全局筛选器
            var result = await _appService.GetListAsync(new PagedAndSortedResultRequestDto
            {
                SkipCount = 0,
                MaxResultCount = 100,
            });
            result.TotalCount.ShouldBe(1);
            result.Items.ShouldAllBe(x => x.OrganizationUnitId == ouAId);
        }
    }

    [Fact]
    public async Task Should_Auto_Fill_Organization_Unit_On_Create()
    {
        var (ouAId, _, _, _, _) = await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: new[] { ouAId }, userId: null)))
        {
            // 不传组织：写入侧应从快照自动取第一个可见组织
            var created = await _appService.CreateAsync(new CreateBizDataScopeDemoDto { Name = "AutoFill" });
            created.OrganizationUnitId.ShouldBe(ouAId);
        }
    }

    [Fact]
    public async Task Should_Keep_Explicit_Organization_Unit_On_Create()
    {
        var (ouAId, ouBId, _, _, _) = await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: new[] { ouAId, ouBId }, userId: null)))
        {
            // 显式指定 OU-B（在范围内、但不是快照第一个可见组织 OU-A）：显式赋值不覆盖
            var created = await _appService.CreateAsync(new CreateBizDataScopeDemoDto
            {
                Name = "Explicit",
                OrganizationUnitId = ouBId,
            });
            created.OrganizationUnitId.ShouldBe(ouBId);
        }
    }

    [Fact]
    public async Task Should_Reject_Organization_Unit_Out_Of_Scope_On_Create()
    {
        var (ouAId, ouBId, _, _, _) = await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: new[] { ouAId }, userId: null)))
        {
            // 显式指定范围外的 OU-B：写入侧防线拒绝（归属伪造）
            await Should.ThrowAsync<UserFriendlyException>(() =>
                _appService.CreateAsync(new CreateBizDataScopeDemoDto
                {
                    Name = "OutOfScope",
                    OrganizationUnitId = ouBId,
                }));
        }
    }

    [Fact]
    public async Task Should_Reject_Nonexistent_Organization_Unit_When_IsAll_On_Create()
    {
        await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: true, selfOnly: false, organizationUnitIds: Array.Empty<Guid>(), userId: null)))
        {
            // All 范围没有可见集合可比对，但组织必须存在——任意 GUID 拒绝
            await Should.ThrowAsync<UserFriendlyException>(() =>
                _appService.CreateAsync(new CreateBizDataScopeDemoDto
                {
                    Name = "Nonexistent",
                    OrganizationUnitId = Guid.NewGuid(),
                }));
        }
    }

    [Fact]
    public async Task Should_Throw_When_Organization_Unresolvable()
    {
        await SeedTestDataAsync();

        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: Array.Empty<Guid>(), userId: null)))
        {
            // 非 All、非 SelfOnly、可见组织为空、调用方未显式指定 → 算不出组织，抛业务异常
            var ex = await Should.ThrowAsync<BusinessException>(() =>
                _appService.CreateAsync(new CreateBizDataScopeDemoDto { Name = "Unresolvable" }));
            ex.Code.ShouldBe(AbpAdminDomainErrorCodes.DataScopes.CannotResolveOrganizationUnit);
        }
    }

    [Fact]
    public async Task Should_Filter_By_Tenant_And_Ou_Together()
    {
        var (ouAId, ouBId, _, _, _) = await SeedTestDataAsync();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var demoTenantA = Guid.NewGuid();
        var demoTenantB = Guid.NewGuid();

        await WithUnitOfWorkAsync(async () =>
        {
            using (_dataFilter.Disable<IDataScopeEnabled>())
            {
                using (_currentTenant.Change(tenantA))
                {
                    await _repository.InsertAsync(new BizDataScopeDemo(demoTenantA, "Biz-Demo-TenantA", ouAId));
                }

                using (_currentTenant.Change(tenantB))
                {
                    await _repository.InsertAsync(new BizDataScopeDemo(demoTenantB, "Biz-Demo-TenantB", ouBId));
                }
            }
        });

        // 租户筛选器与 OU 筛选器叠加 = 交集：租户 A 只见自己的 OU-A 行（看不见租户 B 的）
        using (_currentTenant.Change(tenantA))
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: new[] { ouAId }, userId: null)))
        {
            var list = await _repository.GetListAsync();
            list.Count.ShouldBe(1);
            list.ShouldContain(x => x.Id == demoTenantA);
        }

        using (_currentTenant.Change(tenantB))
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: false, organizationUnitIds: new[] { ouBId }, userId: null)))
        {
            var list = await _repository.GetListAsync();
            list.Count.ShouldBe(1);
            list.ShouldContain(x => x.Id == demoTenantB);
        }
    }

    /// <summary>构造指定 UserId 的测试主体（审计属性 CreatorId/TenantId 由 ABP 从当前主体回填）。</summary>
    private static ClaimsPrincipal CreatePrincipal(Guid userId)
    {
        return new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(AbpClaimTypes.UserId, userId.ToString()) },
            authenticationType: "Testing"));
    }
}
