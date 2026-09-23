using System;

namespace AbpAdmin.ClickHouse;

/// <summary>
/// 采集事件（CH 行模型）。Payload 放 JSON 字符串，查询侧用 CH 的 JSON 函数展开；
/// 需要高频过滤/聚合的维度应提为独立列（MergeTree 支持 ALTER ADD COLUMN，随建表 DDL 一并演进）。
/// </summary>
public class CollectedEvent
{
    /// <summary>租户 Id，空串表示宿主/无租户</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>事件类型，如 device.heartbeat、http.access</summary>
    public string EventName { get; set; } = string.Empty;

    /// <summary>采集来源标识，如服务名/网关名</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>事件体（JSON）</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// 事件发生时间。必须是 Kind=Utc 的 DateTime(默认 DateTime.UtcNow):
    /// CH 列 DateTime64(3) 按 server 时区解释,Kind=Unspecified/Local 的值会被驱动
    /// 按本机时区换算,产生小时级偏移。
    /// </summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}
