using System;
using Volo.Abp;
using Volo.Abp.Domain.Entities.Auditing;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Timing;

namespace AbpAdmin.Notifications;

/// <summary>
/// 广播通知批次（T3.5 第 10 步）。EasyAbp 的 NotificationInfo 没有批次/续跑游标/计数，
/// 这张自研表存"一份正文 + keyset 游标 + 计数"，支撑分批 fan-out 与批次级幂等续跑：
/// 作业失败重试时从库里的 LastProcessedUserId 续，不会给已处理用户重复发。
/// </summary>
public class NotificationBroadcast : AuditedAggregateRoot<Guid>, IMultiTenant
{
    public virtual Guid? TenantId { get; protected set; }

    /// <summary>全部用户 / 角色 / 组织单元，见 <see cref="NotificationBroadcastTargetTypes"/>。</summary>
    public virtual string TargetType { get; protected set; } = default!;

    /// <summary>角色 Id 或组织单元 Id；TargetType = All 时为 null。</summary>
    public virtual Guid? TargetId { get; protected set; }

    /// <summary>渠道，逗号分隔（Mailing,Sms,InApp），与 T3.4 字典 NotificationMethod 对齐。</summary>
    public virtual string NotificationMethods { get; protected set; } = default!;

    public virtual string Title { get; protected set; } = default!;

    public virtual string Body { get; protected set; } = default!;

    /// <summary>含 Sms 渠道时的短信内容/模板参数 JSON（阿里云端即 TemplateParam）。</summary>
    public virtual string? SmsText { get; protected set; }

    /// <summary>含 Sms 渠道时的渠道属性（SignName/TemplateCode/TemplateID/TemplateParamSet），JSON 序列化。</summary>
    public virtual string? SmsPropertiesJson { get; protected set; }

    /// <summary>目标用户总数（创建时 COUNT 得到，供进度展示）。</summary>
    public virtual int TotalCount { get; protected set; }

    /// <summary>已成功创建通知记录的用户数（每批提交后累加）。</summary>
    public virtual int SentCount { get; protected set; }

    /// <summary>
    /// 预留计数位。当前批次是原子的（一批一个 UoW，整批成功或整批回滚），
    /// 批内单用户失败会拖垮整批交给 ABP 作业重试，所以此计数恒为 0。
    /// </summary>
    public virtual int FailedCount { get; protected set; }

    /// <summary>keyset 游标：上一批处理到的用户 Id。作业从这里续跑而不是从 args（批次级幂等）。</summary>
    public virtual Guid? LastProcessedUserId { get; protected set; }

    /// <summary>Pending / Running / Completed / Failed，见 <see cref="NotificationBroadcastStates"/>。</summary>
    public virtual string State { get; protected set; } = default!;

    public virtual DateTime? CompletionTime { get; protected set; }

    protected NotificationBroadcast()
    {
    }

    public NotificationBroadcast(
        Guid id,
        Guid? tenantId,
        string targetType,
        Guid? targetId,
        string notificationMethods,
        string title,
        string body,
        string? smsText,
        string? smsPropertiesJson,
        int totalCount) : base(id)
    {
        TenantId = tenantId;
        TargetType = Check.NotNullOrWhiteSpace(targetType, nameof(targetType), NotificationBroadcastConsts.MaxTargetTypeLength);
        TargetId = targetId;
        NotificationMethods = Check.NotNullOrWhiteSpace(notificationMethods, nameof(notificationMethods), NotificationBroadcastConsts.MaxNotificationMethodsLength);
        Title = Check.NotNullOrWhiteSpace(title, nameof(title), NotificationBroadcastConsts.MaxTitleLength);
        Body = Check.NotNullOrWhiteSpace(body, nameof(body));
        SmsText = smsText;
        SmsPropertiesJson = smsPropertiesJson;
        TotalCount = totalCount;
        State = NotificationBroadcastStates.Pending;
    }

    public void Start()
    {
        State = NotificationBroadcastStates.Running;
    }

    /// <summary>推进游标并累计本批成功数。</summary>
    public void Advance(Guid lastProcessedUserId, int batchSentCount)
    {
        LastProcessedUserId = lastProcessedUserId;
        SentCount += batchSentCount;
    }

    public void Complete(IClock clock)
    {
        State = NotificationBroadcastStates.Completed;
        CompletionTime = clock.Now;
    }

    /// <summary>防御性终态：仅当记录本身损坏（渠道为空等确定性错误）时置 Failed。瞬时异常不走这里（交给作业重试）。</summary>
    public void Fail(IClock clock)
    {
        State = NotificationBroadcastStates.Failed;
        CompletionTime = clock.Now;
    }
}

public static class NotificationBroadcastConsts
{
    public const int MaxTargetTypeLength = 32;

    public const int MaxNotificationMethodsLength = 128;

    public const int MaxTitleLength = 256;

    public const int MaxStateLength = 16;
}

public static class NotificationBroadcastTargetTypes
{
    public const string All = "All";

    public const string Role = "Role";

    public const string OrganizationUnit = "OrganizationUnit";
}

public static class NotificationBroadcastStates
{
    public const string Pending = "Pending";

    public const string Running = "Running";

    public const string Completed = "Completed";

    public const string Failed = "Failed";
}
