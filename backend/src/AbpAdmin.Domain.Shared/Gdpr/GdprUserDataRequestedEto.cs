using System;
using Volo.Abp.EventBus;

namespace AbpAdmin.Gdpr;

/// <summary>
/// GDPR 用户数据请求事件。
/// 用户发起个人数据导出请求后发布此事件，各模块订阅此事件收集数据。
/// </summary>
[EventName("AbpAdmin.Gdpr.UserDataRequested")]
public class GdprUserDataRequestedEto
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
}
