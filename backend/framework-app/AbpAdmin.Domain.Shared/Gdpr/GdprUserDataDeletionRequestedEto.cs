using System;
using Volo.Abp.EventBus;

namespace AbpAdmin.Gdpr;

/// <summary>
/// GDPR 用户数据删除请求事件。
/// 用户请求删除账户时发布此事件，各模块订阅此事件删除或匿名化用户数据。
/// </summary>
[EventName("AbpAdmin.Gdpr.UserDataDeletionRequested")]
public class GdprUserDataDeletionRequestedEto
{
    /// <summary>
    /// 租户 ID。
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// 用户 ID。
    /// </summary>
    public Guid UserId { get; set; }
}
