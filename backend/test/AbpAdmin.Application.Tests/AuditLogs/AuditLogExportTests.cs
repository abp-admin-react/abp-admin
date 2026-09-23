using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Account;
using AbpAdmin.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Shouldly;
using Volo.Abp.AuditLogging;
using Volo.Abp.BlobStoring;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Emailing;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.AuditLogs;

/* T2.1 审计日志对标：导出集成测试（03-batch2-pro-parity.md T2.1 验收标准：
 * 「集成测试 AuditLogExportTests.cs 覆盖同步导出、转后台、无权限 403 三条路径」）。
 *
 * - 同步导出：AuditLogExcelBuilder 生成的 xlsx 列头为中文、行数与筛选结果一致。
 * - 转后台：EnqueueExportAsync 返回 Queued；AuditLogExportJob 单测覆盖 >1000 条的
 *   后台作业路径本身（建 BLOB、写 AbpAuditLogExcelFiles 记录、发邮件）。
 * - 无权限 403：测试基建 AddAlwaysAllowAuthorization 让 [Authorize] 总是通过，
 *   真正能落到集成测试的 403 语义是下载端点的跨用户 Forbid（CreatorId != 当前用户）。
 */
public abstract class AuditLogExportTests<TStartupModule> : AuditLogTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IAuditLogAppService _auditLogAppService;
    // 经接口解析（HttpApi 控制器同款注入）：导出构建对外的契约是 IAuditLogExcelBuilder
    private readonly IAuditLogExcelBuilder _excelBuilder;
    private readonly IRepository<AuditLogExcelFile, Guid> _excelFileRepository;
    private readonly IBlobContainer<AuditLogExportBlobContainer> _blobContainer;
    private readonly RecordingEmailSender _emailSender;
    private readonly IStringLocalizer<AbpAdminResource> _localizer;

    protected AuditLogExportTests()
    {
        _auditLogRepository = GetRequiredService<IAuditLogRepository>();
        _auditLogAppService = GetRequiredService<IAuditLogAppService>();
        _excelBuilder = GetRequiredService<IAuditLogExcelBuilder>();
        _excelFileRepository = GetRequiredService<IRepository<AuditLogExcelFile, Guid>>();
        _blobContainer = GetRequiredService<IBlobContainer<AuditLogExportBlobContainer>>();
        _emailSender = (RecordingEmailSender)GetRequiredService<IEmailSender>();
        _localizer = GetRequiredService<IStringLocalizer<AbpAdminResource>>();
    }

    [Fact]
    public async Task BuildAsync_Should_Generate_Excel_With_Chinese_Headers_And_All_Rows()
    {
        // Arrange - 唯一 Url 标记隔离共享库里的其他审计日志
        var marker = $"/export-test/{Guid.NewGuid():N}";
        await CreateAuditLogAsync(DateTime.UtcNow.AddMinutes(-3), marker, executionDuration: 100);
        await CreateAuditLogAsync(DateTime.UtcNow.AddMinutes(-2), marker, executionDuration: 200);
        await CreateAuditLogAsync(DateTime.UtcNow.AddMinutes(-1), marker, executionDuration: 300);

        // Act - 同步导出路径（结果集 ≤ 1000 时 Controller 内部走的就是这个 Builder）。
        // 生产调用方（Controller/AppService）总有环境 UoW，keyset 路径的 Queryable 依赖它——测试对齐
        var bytes = await WithUnitOfWorkAsync(() =>
            _excelBuilder.BuildAsync(new GetAuditLogListInput { Url = marker }, maxResultCount: 1000));

        // Assert
        bytes.ShouldNotBeNull();
        bytes.Length.ShouldBeGreaterThan(0);

        var rows = ParseExcelRows(bytes);
        rows.Count.ShouldBe(3);
        // 列头是中文（T2.1 验收：用 Excel 打开列头是中文）
        var firstRow = rows[0];
        firstRow.Keys.ShouldContain("执行时间");
        firstRow.Keys.ShouldContain("用户名");
        firstRow.Keys.ShouldContain("状态码");
        firstRow.Keys.ShouldContain("耗时毫秒");
        firstRow["用户名"].ToString().ShouldBe("admin");
        firstRow["地址"].ToString().ShouldBe(marker);
    }

    [Fact]
    public async Task EnqueueExportAsync_Should_Return_Queued_Status()
    {
        // Act - 转后台路径
        var result = await _auditLogAppService.EnqueueExportAsync(new GetAuditLogListInput());

        // Assert
        result.ShouldNotBeNull();
        result.IsQueued.ShouldBeTrue();
    }

    [Fact]
    public async Task ExportJob_Should_Save_Blob_Insert_Record_And_Send_Email()
    {
        // Arrange - >1000 条转后台后由该作业完成导出，这里单测作业本身
        var marker = $"/export-job-test/{Guid.NewGuid():N}";
        await CreateAuditLogAsync(DateTime.UtcNow.AddMinutes(-2), marker);
        await CreateAuditLogAsync(DateTime.UtcNow.AddMinutes(-1), marker);
        _emailSender.Clear();

        var job = GetRequiredService<AuditLogExportJob>();
        var args = new AuditLogExportJobArgs
        {
            TenantId = null,
            UserId = AdminUserId,
            Email = "audit-export@test.local",
            Filter = new GetAuditLogListInput { Url = marker }
        };

        // Act
        await WithUnitOfWorkAsync(() => job.ExecuteAsync(args));

        // Assert 1: AbpAuditLogExcelFiles 出现记录
        var excelFile = await WithUnitOfWorkAsync(async () =>
        {
            var queryable = await _excelFileRepository.GetQueryableAsync();
            return await _excelFileRepository.AsyncExecuter.FirstOrDefaultAsync(
                queryable.Where(x => x.CreatorId == AdminUserId && x.FileName.StartsWith("audit-logs-"))
                    .OrderByDescending(x => x.CreationTime));
        });
        excelFile.ShouldNotBeNull();

        // Assert 2: BLOB 容器出现对应对象（BLOB 名 = 记录 Id），内容可解析且行数正确
        (await _blobContainer.ExistsAsync(excelFile.Id.ToString())).ShouldBeTrue();
        var bytes = await _blobContainer.GetAllBytesAsync(excelFile.Id.ToString());
        ParseExcelRows(bytes).Count.ShouldBe(2);

        // Assert 3: 发送了完成通知邮件（主题已本地化，按当前测试文化的本地化值断言）
        var expectedSubject = _localizer["AuditLogExport:EmailSubject"].Value;
        _emailSender.SentMessages.ShouldContain(x =>
            x.To == "audit-export@test.local" && x.Subject == expectedSubject);
    }

    [Fact]
    public async Task GetExportFile_Should_Return_Forbid_For_Other_Users_File()
    {
        // Arrange - 另一个用户的导出记录
        var fileId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            await _excelFileRepository.InsertAsync(new AuditLogExcelFile(
                fileId, "audit-logs-other.xlsx", tenantId: null, creatorId: Guid.NewGuid()));
        });
        await _blobContainer.SaveAsync(fileId.ToString(), new byte[] { 1, 2, 3 });

        var controller = CreateExportController();

        // Act - 当前身份是 admin（FakeCurrentPrincipalAccessor），CreatorId 不是 admin
        var result = await controller.GetExportFileAsync(fileId);

        // Assert - 跨用户下载被拒（HTTP 层即 403）
        result.ShouldBeOfType<ForbidResult>();
    }

    [Fact]
    public async Task GetExportFile_Should_Return_NotFound_For_Unknown_Id()
    {
        // 渗透测试回归：MVC 控制器链路上 EntityNotFoundException 不会映射成 404，
        // 旧实现仓储 GetAsync 对不存在的 id 直接 500（枚举探测面）
        var controller = CreateExportController();

        var result = await controller.GetExportFileAsync(Guid.NewGuid());

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetExportFile_Should_Return_NotFound_When_Blob_Missing()
    {
        // 记录在、blob 已丢（如存储端手工清理）：应 404 死链语义而非 500
        var fileId = Guid.NewGuid();
        await WithUnitOfWorkAsync(async () =>
        {
            await _excelFileRepository.InsertAsync(new AuditLogExcelFile(
                fileId, "audit-logs-admin-orphan.xlsx", tenantId: null, creatorId: AdminUserId));
        });

        var controller = CreateExportController();

        var result = await controller.GetExportFileAsync(fileId);

        result.ShouldBeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetExportFile_Should_Return_File_For_Owner()
    {
        // Arrange - 当前用户（admin）自己的导出记录
        var fileId = Guid.NewGuid();
        var content = new byte[] { 9, 8, 7 };
        await WithUnitOfWorkAsync(async () =>
        {
            await _excelFileRepository.InsertAsync(new AuditLogExcelFile(
                fileId, "audit-logs-mine.xlsx", tenantId: null, creatorId: AdminUserId));
        });
        await _blobContainer.SaveAsync(fileId.ToString(), content);

        var controller = CreateExportController();

        // Act
        var result = await controller.GetExportFileAsync(fileId);

        // Assert
        var fileResult = result.ShouldBeOfType<FileContentResult>();
        fileResult.FileContents.ShouldBe(content);
        fileResult.FileDownloadName.ShouldBe("audit-logs-mine.xlsx");
    }

    /// <summary>
    /// 手动构造控制器并接上测试环境的 LazyServiceProvider（CurrentUser 等服务由此解析）。
    /// </summary>
    private AuditLogExportController CreateExportController()
    {
        var controller = new AuditLogExportController(
            _excelFileRepository,
            _blobContainer,
            _excelBuilder,
            _localizer,
            GetRequiredService<AbpAdmin.RateLimiting.IOperationRateLimitingChecker>())
        {
            LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>()
        };
        return controller;
    }
}
