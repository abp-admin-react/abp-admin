using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.AuditLogs;

public interface IAuditLogAppService : IApplicationService
{
    Task<PagedResultDto<AuditLogDto>> GetListAsync(GetAuditLogListInput input);

    Task<AuditLogDto> GetAsync(Guid id);

    /// <summary>标记审计日志（错误）为已处理。幂等：重复标记刷新处理人与备注。</summary>
    Task MarkHandledAsync(Guid id, MarkAuditLogHandledInput input);

    /// <summary>取消「已处理」标记（未标记时静默成功）。</summary>
    Task UnmarkHandledAsync(Guid id);

    /// <summary>
    /// 异步导出审计日志。转后台作业，完成后发邮件通知。
    /// 同步导出请走 Controller 的 GET /api/app/audit-log/export。
    /// </summary>
    Task<AuditLogExportResultDto> EnqueueExportAsync(GetAuditLogListInput input);

    Task<PagedResultDto<EntityChangeHistoryDto>> GetEntityChangeHistoryAsync(GetEntityChangeHistoryInput input);

    Task<List<AuditLogAverageDurationDto>> GetAverageExecutionDurationPerDayAsync(GetAuditLogStatisticsInput input);

    Task<AuditLogErrorRateDto> GetErrorRateAsync(GetAuditLogStatisticsInput input);
}

public class GetAuditLogListInput : PagedAndSortedResultRequestDto
{
    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public string? HttpMethod { get; set; }

    public string? Url { get; set; }

    public string? UserName { get; set; }

    public string? ApplicationName { get; set; }

    public string? CorrelationId { get; set; }

    public HttpStatusCode? HttpStatusCode { get; set; }

    public bool? HasException { get; set; }

    /// <summary>仅看「未处理」的错误日志（HttpStatusCode≥400 或有异常），用于错误认领工作流。</summary>
    public bool? UnhandledErrorOnly { get; set; }
}

public class MarkAuditLogHandledInput
{
    /// <summary>
    /// 处置备注（结论/根因/工单号等，可选）。
    /// DTO 层注解把超长输入转成 400（AppService 里 Check.Length 兜底抛的是 500 级异常，
    /// 兜的是绕过 DTO 注解的直接服务调用）。
    /// </summary>
    [System.ComponentModel.DataAnnotations.StringLength(AuditLogHandleConsts.MaxNoteLength)]
    public string? Note { get; set; }
}

public class AuditLogDto : EntityDto<Guid>
{
    public string? ApplicationName { get; set; }

    public string? UserName { get; set; }

    public DateTime ExecutionTime { get; set; }

    public int ExecutionDuration { get; set; }

    public string? ClientIpAddress { get; set; }

    /// <summary>客户端 Id（OpenIddict client_id，详情页展示用；匿名/用户令牌请求可为空）。</summary>
    public string? ClientId { get; set; }

    public string? HttpMethod { get; set; }

    public string? Url { get; set; }

    public string? Exceptions { get; set; }

    public int? HttpStatusCode { get; set; }

    public string? CorrelationId { get; set; }

    /// <summary>IP 归属地（展示字段，由应用服务按当页 IP 批量解析后填充）。</summary>
    public string? IpLocation { get; set; }

    /// <summary>是否已被标记处理（行内 HandledAt 映射列非空即视为已处理）。</summary>
    public bool IsHandled { get; set; }

    public string? HandledBy { get; set; }

    public DateTime? HandledAt { get; set; }

    public string? HandleNote { get; set; }

    public List<AuditLogActionDto> Actions { get; set; } = new();

    public List<EntityChangeDto> EntityChanges { get; set; } = new();
}

public class AuditLogActionDto
{
    public string? ServiceName { get; set; }

    public string? MethodName { get; set; }

    public int ExecutionDuration { get; set; }
}

public class EntityChangeDto
{
    public string? EntityTypeFullName { get; set; }

    public string? EntityId { get; set; }

    public byte ChangeType { get; set; }

    public List<EntityPropertyChangeDto> PropertyChanges { get; set; } = new();
}

public class EntityPropertyChangeDto
{
    public string? PropertyName { get; set; }

    public string? OriginalValue { get; set; }

    public string? NewValue { get; set; }
}

public class GetEntityChangeHistoryInput : PagedAndSortedResultRequestDto
{
    public string EntityTypeFullName { get; set; } = default!;

    public string EntityId { get; set; } = default!;
}

public class EntityChangeHistoryDto
{
    public Guid Id { get; set; }

    public Guid AuditLogId { get; set; }

    public DateTime ChangeTime { get; set; }

    public byte ChangeType { get; set; }

    public string? EntityTypeFullName { get; set; }

    public string? EntityId { get; set; }

    public string? UserName { get; set; }

    public List<EntityPropertyChangeDto> PropertyChanges { get; set; } = new();
}

public class GetAuditLogStatisticsInput
{
    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }
}

public class AuditLogAverageDurationDto
{
    public DateTime Date { get; set; }

    public double AvgExecutionDuration { get; set; }
}

public class AuditLogErrorRateDto
{
    public long TotalCount { get; set; }

    public long ErrorCount { get; set; }

    public double ErrorRate { get; set; }

    public List<AuditLogErrorRateDataPoint> DataPoints { get; set; } = new();
}

public class AuditLogErrorRateDataPoint
{
    public DateTime Date { get; set; }

    public long TotalCount { get; set; }

    public long ErrorCount { get; set; }

    public double ErrorRate { get; set; }
}
