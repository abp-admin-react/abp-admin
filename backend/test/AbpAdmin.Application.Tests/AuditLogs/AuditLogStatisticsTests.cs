using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Shouldly;
using Volo.Abp.Auditing;
using Volo.Abp.AuditLogging;
using Volo.Abp.Guids;
using Volo.Abp.Modularity;
using Xunit;

namespace AbpAdmin.AuditLogs;

/* T2.1 审计日志对标：统计端点与单实体变更历史集成测试。
 * 对应 03-batch2-pro-parity.md T2.1 第 8/10 项：
 * - GetAverageExecutionDurationPerDayAsync / GetErrorRateAsync 的聚合结果正确
 *   （聚合下推数据库侧的验收靠 MCP 抓 SQL，这里验证结果语义）；
 * - GetEntityChangeHistoryAsync 返回该实体的全部历史变更（跨多条审计日志），
 *   倒序分页、含属性变更明细与操作人用户名。
 *
 * 统计用例固定在 2020 年的时间窗口：共享测试库中其他用例插入的日志都在「当下」，
 * 不会落进窗口污染聚合结果。
 */
public abstract class AuditLogStatisticsTests<TStartupModule> : AuditLogTestBase<TStartupModule>
    where TStartupModule : IAbpModule
{
    private readonly IAuditLogAppService _auditLogAppService;
    private readonly IGuidGenerator _guidGenerator;

    protected AuditLogStatisticsTests()
    {
        _auditLogAppService = GetRequiredService<IAuditLogAppService>();
        _guidGenerator = GetRequiredService<IGuidGenerator>();
    }

    [Fact]
    public async Task GetAverageExecutionDurationPerDayAsync_Should_Compute_Per_Day_Averages()
    {
        // Arrange
        var day1 = new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var day2 = new DateTime(2020, 1, 2, 10, 0, 0, DateTimeKind.Utc);
        var marker = $"/stats-test/{Guid.NewGuid():N}";

        await CreateAuditLogAsync(day1, marker, executionDuration: 100);
        await CreateAuditLogAsync(day1.AddHours(1), marker, executionDuration: 300);
        await CreateAuditLogAsync(day2, marker, executionDuration: 600);

        // Act
        var result = await _auditLogAppService.GetAverageExecutionDurationPerDayAsync(
            new GetAuditLogStatisticsInput
            {
                StartTime = day1.AddDays(-1),
                EndTime = day2.AddDays(1)
            });

        // Assert
        result.Count.ShouldBe(2);
        result[0].Date.Date.ShouldBe(day1.Date);
        result[0].AvgExecutionDuration.ShouldBe(200); // (100 + 300) / 2
        result[1].Date.Date.ShouldBe(day2.Date);
        result[1].AvgExecutionDuration.ShouldBe(600);
    }

    [Fact]
    public async Task GetErrorRateAsync_Should_Compute_Error_Ratio_And_Data_Points()
    {
        // Arrange - 错误口径：HttpStatusCode >= 400 或 Exceptions 非空
        var day1 = new DateTime(2020, 3, 1, 10, 0, 0, DateTimeKind.Utc);
        var marker = $"/stats-test/{Guid.NewGuid():N}";

        await CreateAuditLogAsync(day1, marker, httpStatusCode: 200);
        await CreateAuditLogAsync(day1.AddHours(1), marker, httpStatusCode: 500);
        await CreateAuditLogAsync(day1.AddHours(2), marker, httpStatusCode: 200, exceptions: "boom");

        // Act
        var result = await _auditLogAppService.GetErrorRateAsync(
            new GetAuditLogStatisticsInput
            {
                StartTime = day1.AddDays(-1),
                EndTime = day1.AddDays(1)
            });

        // Assert
        result.TotalCount.ShouldBe(3);
        result.ErrorCount.ShouldBe(2);
        result.ErrorRate.ShouldBe(66.67);
        result.DataPoints.Count.ShouldBe(1);
        result.DataPoints[0].TotalCount.ShouldBe(3);
        result.DataPoints[0].ErrorCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetEntityChangeHistoryAsync_Should_Return_All_Changes_Of_The_Entity()
    {
        // Arrange - 同一实体横跨两条审计日志的两次变更
        var entityId = Guid.NewGuid().ToString();
        const string entityType = "Volo.Abp.Identity.IdentityUser";
        var t1 = new DateTime(2020, 2, 1, 10, 0, 0, DateTimeKind.Utc);
        var t2 = t1.AddMinutes(5);
        var marker = $"/history-test/{Guid.NewGuid():N}";

        var log1Id = Guid.NewGuid();
        await CreateAuditLogAsync(t1, marker, entityChanges: new List<EntityChange>
        {
            CreateEntityChange(log1Id, entityType, entityId, t1, EntityChangeType.Created,
                propertyName: "UserName", originalValue: null, newValue: "alice")
        }, id: log1Id);
        var log2Id = Guid.NewGuid();
        await CreateAuditLogAsync(t2, marker, entityChanges: new List<EntityChange>
        {
            CreateEntityChange(log2Id, entityType, entityId, t2, EntityChangeType.Updated,
                propertyName: "UserName", originalValue: "alice", newValue: "bob")
        }, id: log2Id);
        // 干扰项：另一条实体的变更不应出现在结果里
        var log3Id = Guid.NewGuid();
        await CreateAuditLogAsync(t2, marker, entityChanges: new List<EntityChange>
        {
            CreateEntityChange(log3Id, entityType, Guid.NewGuid().ToString(), t2, EntityChangeType.Updated,
                propertyName: "UserName", originalValue: "x", newValue: "y")
        }, id: log3Id);

        // Act
        var result = await _auditLogAppService.GetEntityChangeHistoryAsync(
            new GetEntityChangeHistoryInput
            {
                EntityTypeFullName = entityType,
                EntityId = entityId,
                MaxResultCount = 10
            });

        // Assert - 是该实体的全部历史变更（不止某一条审计日志内的），按 ChangeTime 倒序
        result.TotalCount.ShouldBe(2);
        result.Items.Count.ShouldBe(2);
        result.Items[0].ChangeTime.ShouldBe(t2);
        result.Items[1].ChangeTime.ShouldBe(t1);
        result.Items[0].ChangeType.ShouldBe((byte)EntityChangeType.Updated);
        result.Items[0].UserName.ShouldBe("admin");
        result.Items[0].AuditLogId.ShouldNotBe(Guid.Empty);

        var propertyChange = result.Items[0].PropertyChanges.ShouldHaveSingleItem();
        propertyChange.PropertyName.ShouldBe("UserName");
        propertyChange.OriginalValue.ShouldBe("alice");
        propertyChange.NewValue.ShouldBe("bob");
    }

    private EntityChange CreateEntityChange(
        Guid auditLogId,
        string entityType,
        string entityId,
        DateTime changeTime,
        EntityChangeType changeType,
        string propertyName,
        string? originalValue,
        string? newValue)
    {
        var changeInfo = new EntityChangeInfo
        {
            ChangeTime = changeTime,
            ChangeType = changeType,
            EntityId = entityId,
            EntityTypeFullName = entityType,
            PropertyChanges = new List<EntityPropertyChangeInfo>
            {
                new()
                {
                    PropertyName = propertyName,
                    PropertyTypeFullName = typeof(string).FullName!,
                    OriginalValue = originalValue,
                    NewValue = newValue
                }
            }
        };

        // EntityChange.AuditLogId 必须与所属 AuditLog.Id 一致，调用方先定日志 Id 再传进来
        return new EntityChange(_guidGenerator, auditLogId, changeInfo, tenantId: null);
    }
}
