using System;
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
using Xunit;

namespace AbpAdmin.Biz.Template.DataScopes;

/* 数据权限示例（模块侧落地）的过滤契约：镜像框架 DataScopeFilterTests 的模拟手法——
 * ICurrentDataScopeState.Change 注入数据范围快照，验证模块 BizTemplateDbContext 里的
 * 数据范围全局筛选器与写入侧自动填充。语义与框架逐条对齐：
 * 1. IsAll 看全部（含 null OU 行）
 * 2. 指定 OU 只看对应行，null OU 行不可见（fail-closed）
 * 3. 可见组织集合为空 = 零行可见
 * 4. SelfOnly 与机构范围是「或」关系
 * 5. IDataFilter.Disable<IDataScopeEnabled> 旁路（种子/后台作业姿势）
 * 6. AppService 查询同样吃到过滤（业务面零过滤代码）
 * 7. 写入侧：快照可算出组织 → 自动填充；算不出 → 抛业务异常，不静默写 null
 */
public class BizDataScopeDemoFilterTests : BizTemplateTestBase
{
    private readonly IRepository<BizDataScopeDemo, Guid> _repository;
    private readonly IBizDataScopeDemoAppService _appService;
    private readonly ICurrentDataScopeState _dataScopeState;
    private readonly IDataFilter _dataFilter;

    public BizDataScopeDemoFilterTests()
    {
        _repository = GetRequiredService<IRepository<BizDataScopeDemo, Guid>>();
        _appService = GetRequiredService<IBizDataScopeDemoAppService>();
        _dataScopeState = GetRequiredService<ICurrentDataScopeState>();
        _dataFilter = GetRequiredService<IDataFilter>();
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
    public async Task Should_Return_Ou_Or_Self_When_SelfOnly_With_Ou()
    {
        var (ouAId, _, demoAId, _, _) = await SeedTestDataAsync();
        var userId = Guid.NewGuid();

        // SelfOnly + OU-A：OU-A 的记录 + 自己创建的记录（测试数据无创建者，只剩 OU-A 分支）
        using (_dataScopeState.Change(new DataScopeSnapshot(
            isAll: false, selfOnly: true, organizationUnitIds: new[] { ouAId }, userId: userId)))
        {
            var list = await _repository.GetListAsync();
            list.Count.ShouldBe(1);
            list.ShouldContain(x => x.Id == demoAId);
        }
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
}
