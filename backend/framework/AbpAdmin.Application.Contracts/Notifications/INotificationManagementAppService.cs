using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Notifications;

/// <summary>
/// 管理侧通知服务（T3.5 第 11/12 步）。全部端点要 EasyAbp.NotificationService.Notification.Manage。
/// 不用模块的 INotificationAppService 做列表，因为它的筛选表达不了"默认排除重试记录"
/// （RetryForNotificationId == null）且给不出每行的重试次数/最终状态（3.9.0 反编译核实其
/// NotificationGetListInput 的 RetryForNotificationId 只能等值过滤）。
/// 模块自己的只读端点仍然暴露（/api/notification-service/notification），Manage 约束不变。
/// </summary>
public interface INotificationManagementAppService : IApplicationService
{
    Task<PagedResultDto<NotificationListItemDto>> GetListAsync(GetNotificationListInput input);

    /// <summary>详情 + 完整尝试链（原记录 + 所有重试记录，按时间升序）。</summary>
    Task<NotificationDetailDto> GetAsync(Guid id);

    /// <summary>手动重试：新建一条 RetryForNotificationId 指向原记录的通知，走模块的发送管线。</summary>
    Task RetryAsync(Guid id);

    /// <summary>发公告（fan-out 后台作业分批），返回批次 id。</summary>
    Task<Guid> BroadcastAsync(BroadcastNotificationInput input);

    /// <summary>手动发送给指定用户（逐渠道调 INotificationDispatcher）。</summary>
    Task SendToUsersAsync(SendToUsersNotificationInput input);

    /// <summary>查广播批次进度。</summary>
    Task<NotificationBroadcastDto> GetBroadcastAsync(Guid id);
}

public class SendToUsersNotificationInput
{
    [Required]
    public List<Guid> UserIds { get; set; } = new();

    /// <summary>渠道集合：Mailing / Sms / InApp，至少一个。</summary>
    [Required]
    public List<string> NotificationMethods { get; set; } = new();

    [Required]
    public string Title { get; set; } = default!;

    [Required]
    public string Body { get; set; } = default!;

    public string? SmsText { get; set; }

    public Dictionary<string, object>? SmsProperties { get; set; }
}

public class GetNotificationListInput : PagedAndSortedResultRequestDto
{
    public string? NotificationMethod { get; set; }

    public bool? Success { get; set; }

    /// <summary>只看待发送（Success == null）。与 Success 二选一（Success 优先）。</summary>
    public bool PendingOnly { get; set; }

    public string? UserName { get; set; }

    public DateTime? CreationTimeStart { get; set; }

    public DateTime? CreationTimeEnd { get; set; }

    /// <summary>是否包含重试记录（RetryForNotificationId != null 的行）。默认 false——
    /// 列表默认只显示原始通知，避免重试记录把列表刷满（规格第 12 步）。</summary>
    public bool IncludeRetries { get; set; }
}

[Serializable]
public class NotificationListItemDto
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string? UserName { get; set; }

    public Guid NotificationInfoId { get; set; }

    public string? NotificationMethod { get; set; }

    /// <summary>null = 待发送。</summary>
    public bool? Success { get; set; }

    public DateTime? CompletionTime { get; set; }

    public string? FailureReason { get; set; }

    public DateTime CreationTime { get; set; }

    /// <summary>该原始通知的重试次数。</summary>
    public int RetryCount { get; set; }

    /// <summary>最终状态：有重试时取最后一次尝试的 Success，否则取本行的 Success。</summary>
    public bool? FinalSuccess { get; set; }
}

[Serializable]
public class NotificationDetailDto : NotificationListItemDto
{
    public Guid? RetryForNotificationId { get; set; }

    /// <summary>NotificationInfo 的 ExtraProperties（标题/正文等都在里面——模块没有结构化正文列）。</summary>
    public Dictionary<string, object?>? NotificationInfoProperties { get; set; }

    /// <summary>完整尝试链（含本记录；原记录在前，重试按时间升序）。</summary>
    public List<NotificationAttemptDto> Attempts { get; set; } = new();
}

[Serializable]
public class NotificationAttemptDto
{
    public Guid Id { get; set; }

    public bool? Success { get; set; }

    public DateTime? CompletionTime { get; set; }

    public string? FailureReason { get; set; }

    public DateTime CreationTime { get; set; }
}

[Serializable]
public class NotificationBroadcastDto
{
    public Guid Id { get; set; }

    public string? TargetType { get; set; }

    public Guid? TargetId { get; set; }

    public string? NotificationMethods { get; set; }

    public string? Title { get; set; }

    public string? Body { get; set; }

    public int TotalCount { get; set; }

    public int SentCount { get; set; }

    public int FailedCount { get; set; }

    public string? State { get; set; }

    public DateTime CreationTime { get; set; }

    public DateTime? CompletionTime { get; set; }
}
