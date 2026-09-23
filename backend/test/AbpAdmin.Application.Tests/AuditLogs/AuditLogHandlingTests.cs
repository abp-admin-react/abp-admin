using System;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.AuditLogs;

/* 错误日志「已处理」工作流契约测试（对标 ruoyi ApiErrorLog 标记已处理）：
 * 处理状态是 AuditLog 行内扩展属性映射列（AuditLogHandleConsts 四列，无侧表）。
 * 断言口径：经仓储重新取实体读 GetProperty（走 ABP track 时列→字典回填，
 * 等价于验证列已持久化）、DTO 展示字段、「仅未处理」单表筛选。
 */
public abstract class AuditLogHandlingTests<TStartupModule> : AuditLogTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IAuditLogAppService _appService;
    private readonly IAuditLogRepository _auditLogRepository;

    protected AuditLogHandlingTests()
    {
        _appService = GetRequiredService<IAuditLogAppService>();
        _auditLogRepository = GetRequiredService<IAuditLogRepository>();
    }

    /// <summary>重新取实体并读处理状态扩展属性（新 track = 列值回填，验证持久化而非工作单元缓存）。</summary>
    private async Task<(DateTime? HandledAt, string? ByName, string? Note)> FetchHandledAsync(Guid logId)
    {
        return await WithUnitOfWorkAsync(async () =>
        {
            var log = await _auditLogRepository.FindAsync(logId);
            return (
                log!.GetProperty<DateTime?>(AuditLogHandleConsts.HandledAtPropertyName),
                log.GetProperty<string>(AuditLogHandleConsts.HandledByNamePropertyName),
                log.GetProperty<string>(AuditLogHandleConsts.HandledNotePropertyName));
        });
    }

    [Fact]
    public async Task Mark_And_Unmark_Should_Be_Idempotent()
    {
        var log = await CreateAuditLogAsync(DateTime.Now, $"handle-workflow-{Guid.NewGuid():N}", httpStatusCode: 500);

        // 首次标记 → 行内四列写入
        await _appService.MarkHandledAsync(log.Id, new MarkAuditLogHandledInput { Note = "已重启服务" });
        var (handledAt, byName, note) = await FetchHandledAsync(log.Id);
        handledAt.ShouldNotBeNull();
        byName.ShouldNotBeNull();
        note.ShouldBe("已重启服务");

        // 重复标记 → 同一行覆盖刷新（upsert：行数不变，Note 取最新值）
        await _appService.MarkHandledAsync(log.Id, new MarkAuditLogHandledInput { Note = "结论更新：配置修复" });
        var sameLogCount = (await _auditLogRepository.GetListAsync(x => x.Id == log.Id)).Count;
        sameLogCount.ShouldBe(1);
        (_, _, note) = await FetchHandledAsync(log.Id);
        note.ShouldBe("结论更新：配置修复");

        // 详情能看到处理状态
        var detail = await _appService.GetAsync(log.Id);
        detail.IsHandled.ShouldBeTrue();
        detail.HandledBy.ShouldNotBeNull();
        detail.HandledAt.ShouldNotBeNull();
        detail.HandleNote.ShouldBe("结论更新：配置修复");

        // 取消标记 → 四列清空；再取消 → 静默成功
        await _appService.UnmarkHandledAsync(log.Id);
        await _appService.UnmarkHandledAsync(log.Id);
        var afterUnmark = await _appService.GetAsync(log.Id);
        afterUnmark.IsHandled.ShouldBeFalse();
        afterUnmark.HandledAt.ShouldBeNull();
        afterUnmark.HandleNote.ShouldBeNull();
    }

    [Fact]
    public async Task MarkHandled_Should_Throw_For_Nonexistent_AuditLog()
    {
        // EntityNotFound 语义由主表承担
        await Should.ThrowAsync<Volo.Abp.Domain.Entities.EntityNotFoundException>(() =>
            _appService.MarkHandledAsync(Guid.NewGuid(), new MarkAuditLogHandledInput()));
    }

    [Fact]
    public async Task UnhandledErrorOnly_Should_Respect_HasException_Combination()
    {
        // HasException=false + UnhandledErrorOnly=true：只看「状态码≥400 且无异常」的错误日志
        // （组合筛选与 AuditLogQueryExtensions 逐条对齐，不再是静默吞掉 HasException）
        var marker = $"unhandled-hasex-{Guid.NewGuid():N}";
        var statusError = await CreateAuditLogAsync(DateTime.Now, $"{marker}-500", httpStatusCode: 500);
        var exceptionError = await CreateAuditLogAsync(DateTime.Now, $"{marker}-ex", httpStatusCode: 200, exceptions: "boom");

        var result = await _appService.GetListAsync(new GetAuditLogListInput
        {
            UnhandledErrorOnly = true,
            HasException = false,
            Url = marker,
            MaxResultCount = 10,
        });

        result.TotalCount.ShouldBe(1);
        result.Items.Single().Id.ShouldBe(statusError.Id);
    }

    [Fact]
    public async Task UnhandledErrorOnly_Should_Validate_Sorting_Whitelist()
    {
        // Sorting 是自由文本且直接进 Dynamic LINQ：白名单外输入 → 400（对齐操作日志先例）
        await Should.ThrowAsync<Volo.Abp.Validation.AbpValidationException>(() =>
            _appService.GetListAsync(new GetAuditLogListInput
            {
                UnhandledErrorOnly = true,
                Sorting = "ExecutionTime desc; DROP TABLE AbpAuditLogs",
            }));
    }

    [Fact]
    public async Task AuditLog_Detail_Should_Contain_IpLocation()
    {
        // 审计日志侧的归属地填充（测试库固定 IP 127.0.0.1 → 内网IP 短路，不依赖 xdb）
        var log = await CreateAuditLogAsync(DateTime.Now, $"ip-loc-{Guid.NewGuid():N}", httpStatusCode: 200);

        var detail = await _appService.GetAsync(log.Id);
        detail.IpLocation.ShouldBe("内网IP");
    }

    [Fact]
    public async Task UnhandledErrorOnly_Should_Exclude_Handled_And_Non_Error_Logs()
    {
        var marker = $"unhandled-{Guid.NewGuid():N}";
        var errorLog = await CreateAuditLogAsync(DateTime.Now, $"{marker}-error", httpStatusCode: 500);
        var exceptionLog = await CreateAuditLogAsync(DateTime.Now, $"{marker}-exception", httpStatusCode: 200, exceptions: "boom");
        var normalLog = await CreateAuditLogAsync(DateTime.Now, $"{marker}-normal", httpStatusCode: 200);

        // 初始：两条错误日志均未处理，普通日志不在错误口径内
        var before = await _appService.GetListAsync(new GetAuditLogListInput
        {
            UnhandledErrorOnly = true,
            Url = marker,
            MaxResultCount = 10,
        });
        before.Items.Select(x => x.Url).ShouldBe(
            [$"{marker}-exception", $"{marker}-error"], ignoreOrder: true);

        // 处理其中一条（HandledAt 非空 → 单表条件排除）→ 只剩另一条
        await _appService.MarkHandledAsync(errorLog.Id, new MarkAuditLogHandledInput());
        var after = await _appService.GetListAsync(new GetAuditLogListInput
        {
            UnhandledErrorOnly = true,
            Url = marker,
            MaxResultCount = 10,
        });
        after.TotalCount.ShouldBe(1);
        after.Items.Single().Id.ShouldBe(exceptionLog.Id);

        // 列表页附加状态：普通路径下 handled 信息可见
        var normalView = await _appService.GetListAsync(new GetAuditLogListInput
        {
            Url = marker,
            MaxResultCount = 10,
        });
        var handledDto = normalView.Items.Single(x => x.Id == errorLog.Id);
        handledDto.IsHandled.ShouldBeTrue();
        normalView.Items.Single(x => x.Id == exceptionLog.Id).IsHandled.ShouldBeFalse();

        // 清理，避免污染共享库的后续用例
        await _appService.UnmarkHandledAsync(errorLog.Id);
    }
}
