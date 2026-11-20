using System;
using System.Threading.Tasks;
using AbpAdmin.Grids;
using AbpAdmin.OperationLogs;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;
using Volo.Abp.Validation;
using Xunit;

namespace AbpAdmin.OperationLogs;

/* 查询服务契约（补代码评审 test 透镜 F5）：
 * 1) 注入载荷 Sorting → AbpValidationException（HTTP 400），不再走全局异常处理器 500；
 * 2) 白名单排序/默认排序/Filter 模糊过滤/成功过滤正常工作。 */
public abstract class OperationLogAppServiceTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IOperationLogAppService _appService;
    private readonly IGuidGenerator _guidGenerator;

    protected OperationLogAppServiceTests()
    {
        _appService = GetRequiredService<IOperationLogAppService>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task Injection_Sorting_Should_Throw_ValidationException()
    {
        await Should.ThrowAsync<AbpValidationException>(() =>
            _appService.GetListAsync(new GetOperationLogListInput
            {
                Sorting = "ExecutionTime desc; DROP TABLE AbpUsers",
            }));

        await Should.ThrowAsync<AbpValidationException>(() =>
            _appService.GetListAsync(new GetOperationLogListInput
            {
                Sorting = "new Func(() => 1)",
            }));
    }

    [Fact]
    public async Task Whitelisted_Sorting_And_Filters_Should_Work()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var repository = GetRequiredService<IRepository<OperationLog, Guid>>();
            await repository.InsertAsync(new OperationLog(
                id: _guidGenerator.Create(), type: "测试", subType: "成功操作",
                userId: null, userName: null, tenantId: null, bizId: null,
                action: "内容甲", extra: null, success: true,
                errorMessage: null, requestMethod: "POST", requestUrl: "/api/test",
                clientIpAddress: null, userAgent: null, correlationId: "corr-001",
                duration: 5, executionTime: DateTime.Now.AddMinutes(-2)));
            await repository.InsertAsync(new OperationLog(
                id: _guidGenerator.Create(), type: "测试", subType: "失败操作",
                userId: null, userName: null, tenantId: null, bizId: null,
                action: "内容乙", extra: null, success: false,
                errorMessage: "模拟失败", requestMethod: "POST", requestUrl: "/api/test",
                clientIpAddress: null, userAgent: null, correlationId: "corr-002",
                duration: 50, executionTime: DateTime.Now));
        });

        var byDuration = await _appService.GetListAsync(new GetOperationLogListInput
        {
            Sorting = "Duration asc",
            MaxResultCount = 10,
        });
        byDuration.TotalCount.ShouldBe(2);
        byDuration.Items[0].Duration.ShouldBe(5);

        var defaultSort = await _appService.GetListAsync(new GetOperationLogListInput { MaxResultCount = 10 });
        defaultSort.Items[0].SubType.ShouldBe("失败操作"); // ExecutionTime desc

        var filtered = await _appService.GetListAsync(new GetOperationLogListInput
        {
            Filter = "内容甲",
            Success = true,
        });
        filtered.TotalCount.ShouldBe(1);
        filtered.Items[0].Action.ShouldBe("内容甲");

        // 与审计日志同口径的关联 ID 精确筛选（跨日志串联入口）
        var byCorrelationId = await _appService.GetListAsync(new GetOperationLogListInput
        {
            CorrelationId = "corr-002",
        });
        byCorrelationId.TotalCount.ShouldBe(1);
        byCorrelationId.Items[0].SubType.ShouldBe("失败操作");
        byCorrelationId.Items[0].CorrelationId.ShouldBe("corr-002");
    }

    [Fact]
    public async Task Grid_query_combines_columns_before_sort_count_and_paging()
    {
        var day = new DateTime(2026, 4, 12, 0, 0, 0, DateTimeKind.Utc);
        await WithUnitOfWorkAsync(async () =>
        {
            var repo = GetRequiredService<IRepository<OperationLog, Guid>>();
            foreach (var (name, success, duration, time) in new[]
            {
                ("Alpha", false, 8, day),
                ("Alpha", true, 200, day.AddHours(8)),
                ("Beta", false, 90, day.AddHours(23)),
                ("Other", false, 300, day),
                ("Alpha", false, 100, day.AddDays(1))
            })
            {
                await repo.InsertAsync(new OperationLog(_guidGenerator.Create(), "测试", "示例", null, name, null,
                    null, "用于关键词筛选", null, success, null, null, null, null, null,
                    null, duration, time));
            }
        });

        var input = new GridQueryInput
        {
            Keyword = "关键词",
            MaxResultCount = 1,
            Filters =
            [
                new() { Field = "success", Conditions = [new() { Op = "eq", Value = "false" }] },
                new() { Field = "executionTime", Conditions = [new() { Op = "eq", Value = "2026-04-12" }] },
                new()
                {
                    Field = "userName", Join = "or",
                    Conditions = [new() { Op = "contains", Value = "ALP" }, new() { Op = "eq", Value = "bEtA" }]
                }
            ],
            Sort = [new() { Field = "duration", Direction = "desc" }]
        };
        var first = await _appService.QueryAsync(input);
        first.TotalCount.ShouldBe(2);
        first.Items.ShouldHaveSingleItem().UserName.ShouldBe("Beta");
        input.SkipCount = 1;
        (await _appService.QueryAsync(input)).Items.ShouldHaveSingleItem().UserName.ShouldBe("Alpha");
        input.Keyword = "不在明细中";
        (await _appService.QueryAsync(input)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Grid_query_rejects_unknown_sort_and_incompatible_filter_types()
    {
        await Should.ThrowAsync<AbpValidationException>(() => _appService.QueryAsync(new GridQueryInput
        {
            Sort = [new() { Field = "tenantId", Direction = "asc" }]
        }));
        await Should.ThrowAsync<AbpValidationException>(() => _appService.QueryAsync(new GridQueryInput
        {
            Filters = [new() { Field = "duration", Conditions = [new() { Op = "contains", Value = "8" }] }]
        }));
    }

    [Fact]
    public async Task IpLocation_Should_Be_Resolved_For_Page()
    {
        await WithUnitOfWorkAsync(async () =>
        {
            var repository = GetRequiredService<IRepository<OperationLog, Guid>>();
            await repository.InsertAsync(new OperationLog(
                id: _guidGenerator.Create(), type: "测试", subType: "归属地测试",
                userId: null, userName: null, tenantId: null, bizId: null,
                action: "带IP的日志", extra: null, success: true,
                errorMessage: null, requestMethod: "POST", requestUrl: "/api/test",
                clientIpAddress: "112.17.10.23", userAgent: null, correlationId: "corr-loc",
                duration: 1, executionTime: DateTime.Now));
        });

        var page = await _appService.GetListAsync(new GetOperationLogListInput
        {
            CorrelationId = "corr-loc",
        });
        var dto = page.Items.ShouldHaveSingleItem();
        dto.ClientIpAddress.ShouldBe("112.17.10.23");
        dto.IpLocation.ShouldBe("中国 浙江省 杭州市 电信");
    }
}
