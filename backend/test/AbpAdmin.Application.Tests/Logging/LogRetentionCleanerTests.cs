using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using Shouldly;
using Volo.Abp.AuditLogging;
using Volo.Abp.Data;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Guids;
using Volo.Abp.Linq;
using Volo.Abp.Modularity;
using Volo.Abp.Timing;
using Xunit;

namespace AbpAdmin.Logging;

/* 日志保留期清理契约测试（落库锚点见 EFCore.Tests 的 EfCore 版）：
 * 1) RetentionDays=0/缺省 → 一行不删（缺省安全：不出厂自动删日志）；
 * 2) 到期行删除、保留期内行保留，操作/审计两表独立口径，租户名下行一并清到；
 * 3) 行数 > BatchSize 时分批清空（循环推进正确、总数正确）；负 BatchSize 空转不炸。
 * 行与截止时间之间留 ≥1 天余量，避免时钟推进导致的边界抖动。
 */
public abstract class LogRetentionCleanerTests<TStartupModule> : AbpAdminApplicationTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private const string OpMarker = "保留期清理";
    private const string AuditMarker = "LogRetentionTests";

    private readonly LogRetentionCleaner _cleaner;
    private readonly IRepository<OperationLog, Guid> _operationLogRepository;
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly IClock _clock;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IAsyncQueryableExecuter _asyncExecuter;
    private readonly IDataFilter _dataFilter;

    protected LogRetentionCleanerTests()
    {
        _cleaner = GetRequiredService<LogRetentionCleaner>();
        _operationLogRepository = GetRequiredService<IRepository<OperationLog, Guid>>();
        _auditLogRepository = GetRequiredService<IAuditLogRepository>();
        _clock = GetRequiredService<IClock>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
        _asyncExecuter = GetRequiredService<IAsyncQueryableExecuter>();
        _dataFilter = GetRequiredService<IDataFilter>();
    }

    [Fact]
    public async Task Zero_Retention_Disables_Cleanup()
    {
        var now = _clock.Now;
        await InsertOperationLogAsync(now.AddDays(-365));
        await InsertAuditLogAsync(now.AddDays(-365));

        var removed = await _cleaner.CleanAsync(new LogRetentionOptions
        {
            OperationLogRetentionDays = 0,
            AuditLogRetentionDays = 0,
        });

        removed.ShouldBe(0);
        (await CountOperationLogsAsync()).ShouldBe(1);
        (await CountAuditLogsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Expired_Logs_Deleted_While_Recent_Kept()
    {
        var now = _clock.Now;
        await InsertOperationLogAsync(now.AddDays(-91));
        await InsertOperationLogAsync(now.AddDays(-1));
        await InsertAuditLogAsync(now.AddDays(-91));
        await InsertAuditLogAsync(now.AddDays(-1));

        var removed = await _cleaner.CleanAsync(new LogRetentionOptions
        {
            OperationLogRetentionDays = 90,
            AuditLogRetentionDays = 90,
        });

        removed.ShouldBe(2);
        (await CountOperationLogsAsync()).ShouldBe(1);
        (await CountAuditLogsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Expired_Tenant_Scoped_Rows_Are_Also_Deleted()
    {
        // 全租户清理契约：两张表都是 IMultiTenant，ABP 过滤器在宿主侧只放行 TenantId IS NULL——
        // 清理器必须经 IDataFilter 关闭过滤器，租户名下的过期日志才清得到（否则静默无限增长）。
        var now = _clock.Now;
        var tenantId = Guid.NewGuid();
        await InsertOperationLogAsync(now.AddDays(-91), tenantId);
        await InsertAuditLogAsync(now.AddDays(-91), tenantId);

        var removed = await _cleaner.CleanAsync(new LogRetentionOptions
        {
            OperationLogRetentionDays = 90,
            AuditLogRetentionDays = 90,
        });

        removed.ShouldBe(2);
        (await CountOperationLogsAsync(includeTenantRows: true)).ShouldBe(0);
        (await CountAuditLogsAsync(includeTenantRows: true)).ShouldBe(0);
    }

    [Fact]
    public async Task Negative_BatchSize_Is_Idle_Not_Fatal()
    {
        // 文档语义「≤0 空转不清理」对负值同样成立（负值不钳制会让 Take(-n) 翻译成非法 LIMIT）
        var now = _clock.Now;
        await InsertOperationLogAsync(now.AddDays(-91));

        var removed = await _cleaner.CleanAsync(new LogRetentionOptions
        {
            OperationLogRetentionDays = 90,
            BatchSize = -1,
        });

        removed.ShouldBe(0);
        (await CountOperationLogsAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task More_Audit_Rows_Than_BatchSize_Are_All_Removed()
    {
        // 审计分支的分批循环单独驱动（IAuditLogRepository + DeleteManyAsync 组合与操作日志分支不同）
        var now = _clock.Now;
        for (var i = 0; i < 5; i++)
        {
            await InsertAuditLogAsync(now.AddDays(-31 - i));
        }

        var removed = await _cleaner.CleanAsync(new LogRetentionOptions
        {
            OperationLogRetentionDays = 0,
            AuditLogRetentionDays = 30,
            BatchSize = 2,
        });

        removed.ShouldBe(5);
        (await CountAuditLogsAsync()).ShouldBe(0);
    }

    private Task InsertOperationLogAsync(DateTime executionTime, Guid? tenantId = null)
        => WithUnitOfWorkAsync(() => _operationLogRepository.InsertAsync(new OperationLog(
            _guidGenerator.Create(),
            OpMarker,
            OpMarker,
            userId: null,
            userName: null,
            tenantId: tenantId,
            bizId: null,
            action: "保留期清理用例",
            extra: null,
            success: true,
            errorMessage: null,
            requestMethod: null,
            requestUrl: null,
            clientIpAddress: null,
            userAgent: null,
            correlationId: null,
            duration: 0,
            executionTime: executionTime)));

    /// <summary>
    /// AuditLog 构造函数是 25 参全量签名（含 impersonator 组），属性 set 不可访问，
    /// 只能位置传参：id / applicationName / userId / userName / tenantId / tenantName /
    /// executionTime / executionDuration / clientId / correlationId / clientIpAddress /
    /// browserInfo / clientName / clientLanguage / cultureName / httpStatusCode /
    /// impersonatorUserId / impersonatorUserName / impersonatorTenantId / impersonatorTenantName /
    /// extraProperties / entityChanges / actions / comments / exceptions。
    /// 本用例关心 ExecutionTime、ApplicationName 与 TenantId（清理谓词字段），其余全空。
    /// </summary>
    private Task InsertAuditLogAsync(DateTime executionTime, Guid? tenantId = null)
        => WithUnitOfWorkAsync(() => _auditLogRepository.InsertAsync(new AuditLog(
            _guidGenerator.Create(),
            AuditMarker,
            null, null, tenantId, null,
            executionTime,
            0,
            null, null, null, null, null, null, null,
            null,
            null, null, null, null,
            new ExtraPropertyDictionary(),
            new List<EntityChange>(),
            new List<AuditLogAction>(),
            null, null)));

    /// <summary>includeTenantRows=true 时关闭多租户过滤器统计全部行（验证租户行确被删除）。</summary>
    private Task<int> CountOperationLogsAsync(bool includeTenantRows = false)
        => WithUnitOfWorkAsync(async () =>
        {
            using (includeTenantRows ? _dataFilter.Disable<Volo.Abp.MultiTenancy.IMultiTenant>() : null)
            {
                var queryable = await _operationLogRepository.GetQueryableAsync();
                return await _asyncExecuter.CountAsync(queryable.Where(x => x.Type == OpMarker));
            }
        });

    private Task<int> CountAuditLogsAsync(bool includeTenantRows = false)
        => WithUnitOfWorkAsync(async () =>
        {
            using (includeTenantRows ? _dataFilter.Disable<Volo.Abp.MultiTenancy.IMultiTenant>() : null)
            {
                var queryable = await _auditLogRepository.GetQueryableAsync();
                return await _asyncExecuter.CountAsync(queryable.Where(x => x.ApplicationName == AuditMarker));
            }
        });
}
