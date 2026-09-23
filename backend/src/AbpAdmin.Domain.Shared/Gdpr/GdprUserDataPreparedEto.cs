using System;
using Volo.Abp.EventBus;

namespace AbpAdmin.Gdpr;

/// <summary>
/// GDPR 用户数据准备完成事件。
/// 各模块收集完数据后发布此事件，GDPR 侧订阅此事件保存数据。
/// </summary>
[EventName("AbpAdmin.Gdpr.UserDataPrepared")]
public class GdprUserDataPreparedEto
{
    /// <summary>
    /// 租户 ID。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// GDPR 请求 ID。
    /// </summary>
    public Guid RequestId { get; set; }

    /// <summary>
    /// 用户 ID。
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// 贡献者标识，如 "Identity"、"AuditLogging"。
    /// </summary>
    public string Provider { get; set; } = default!;

    /// <summary>
    /// 该模块的 JSON payload。
    /// </summary>
    public string Data { get; set; } = default!;
}
