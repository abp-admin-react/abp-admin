using System;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.OperationLogs;

public class OperationLogDto : EntityDto<Guid>
{
    public Guid? UserId { get; set; }

    public string? UserName { get; set; }

    public string Type { get; set; } = default!;

    public string SubType { get; set; } = default!;

    public string? BizId { get; set; }

    public string? Action { get; set; }

    public string? Extra { get; set; }

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public string? RequestMethod { get; set; }

    public string? RequestUrl { get; set; }

    public string? ClientIpAddress { get; set; }

    /// <summary>IP 归属地（展示字段，由应用服务按当页 IP 批量解析后填充）。</summary>
    public string? IpLocation { get; set; }

    public string? UserAgent { get; set; }

    /// <summary>请求关联 ID（与审计日志/安全日志同源，可互相串联）。</summary>
    public string? CorrelationId { get; set; }

    public int Duration { get; set; }

    public DateTime ExecutionTime { get; set; }
}

public class GetOperationLogListInput : PagedAndSortedResultRequestDto
{
    /// <summary>模糊过滤：匹配 SubType / Action / BizId / UserName。</summary>
    public string? Filter { get; set; }

    public string? Type { get; set; }

    public string? SubType { get; set; }

    public bool? Success { get; set; }

    public Guid? UserId { get; set; }

    /// <summary>按关联 ID 精确过滤（与审计日志的 CorrelationId 筛选同口径）。</summary>
    public string? CorrelationId { get; set; }

    public DateTime? StartTime { get; set; }

    public DateTime? EndTime { get; set; }
}
