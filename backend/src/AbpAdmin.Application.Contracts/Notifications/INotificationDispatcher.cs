using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using EasyAbp.NotificationService.Notifications;

namespace AbpAdmin.Notifications;

/// <summary>
/// 通知发送门面（T3.5 第 9 步）。业务代码只用这个接口，不直接构造 EasyAbp 的 ETO——
/// ETO 构造函数签名会随模块版本漂移（3.9.0 的短信创建 ETO 就比 README 示例
/// 多出第 5 个 IJsonSerializer 参数），直接构造会让版本升级的编译错误散布到所有调用点。
///
/// 注意：创建走 IDistributedEventBus，当前回落到 LocalDistributedEventBus（进程内），
/// 发布方与 EasyAbp 的订阅方必须在同一进程——单体部署没问题，别当成跨服务解耦。
/// </summary>
public interface INotificationDispatcher
{
    Task SendEmailAsync(SendEmailNotificationInput input);

    Task SendSmsAsync(SendSmsNotificationInput input);

    Task SendInAppAsync(SendInAppNotificationInput input);

    /// <summary>
    /// 广播。目标可以是全部用户、某角色、某组织单元。
    /// 内部走后台作业分批（BroadcastNotificationJob），方法本身立即返回广播批次 id（可查进度）。
    /// 权限（EasyAbp.NotificationService.Notification.Manage）由管理端 AppService 把关，
    /// 本门面只做参数校验——业务代码是服务端可信代码。
    /// </summary>
    Task<Guid> BroadcastAsync(BroadcastNotificationInput input);

    // ========== 批量形态（BroadcastNotificationJob 分批调用） ==========
    // 参数 NotificationUserInfoModel 是 EasyAbp 的公开模型，放 Contracts 无泄漏；
    // 作业按接口依赖才能在测试里替身（见 T3.5 广播测试）。

    /// <summary>一批用户共享一条 NotificationInfo 的邮件创建事件。</summary>
    Task SendEmailBatchAsync(Guid? tenantId, List<NotificationUserInfoModel> users, string subject, string body);

    /// <summary>一批用户共享一条 NotificationInfo 的短信创建事件。</summary>
    Task SendSmsBatchAsync(Guid? tenantId, List<NotificationUserInfoModel> users, string text, IDictionary<string, object> properties);

    /// <summary>一批用户共享一条 NotificationInfo 的站内信创建事件。</summary>
    Task SendInAppBatchAsync(Guid? tenantId, List<NotificationUserInfoModel> users, string title, string body);
}

public class SendEmailNotificationInput
{
    [Required]
    public List<Guid> UserIds { get; set; } = new();

    [Required]
    public string Subject { get; set; } = default!;

    [Required]
    public string Body { get; set; } = default!;
}

public class SendSmsNotificationInput
{
    [Required]
    public List<Guid> UserIds { get; set; } = new();

    /// <summary>
    /// 短信内容。阿里云路径：模板参数 JSON（如 {"code":"123456"}）；腾讯云路径：不读 Text，
    /// 参数走 Properties["TemplateParamSet"]。
    /// </summary>
    [Required]
    public string Text { get; set; } = default!;

    /// <summary>渠道属性：阿里云 SignName/TemplateCode，腾讯云 TemplateID/TemplateParamSet。</summary>
    public Dictionary<string, object>? Properties { get; set; }
}

public class SendInAppNotificationInput
{
    [Required]
    public List<Guid> UserIds { get; set; } = new();

    [Required]
    public string Title { get; set; } = default!;

    [Required]
    public string Body { get; set; } = default!;
}

public class BroadcastNotificationInput
{
    /// <summary>All / Role / OrganizationUnit。</summary>
    [Required]
    public string TargetType { get; set; } = default!;

    /// <summary>角色 Id 或组织单元 Id；TargetType = All 时为 null。</summary>
    public Guid? TargetId { get; set; }

    /// <summary>渠道集合：Mailing / Sms / InApp，至少一个。</summary>
    [Required]
    public List<string> NotificationMethods { get; set; } = new();

    /// <summary>标题（邮件 Subject / 站内信标题）。</summary>
    [Required]
    public string Title { get; set; } = default!;

    /// <summary>正文（邮件 / 站内信）。</summary>
    [Required]
    public string Body { get; set; } = default!;

    /// <summary>含 Sms 渠道时的短信内容/模板参数 JSON；不填则回落用 Body。</summary>
    public string? SmsText { get; set; }

    /// <summary>含 Sms 渠道时的渠道属性（模板编码/签名/参数）。</summary>
    public Dictionary<string, object>? SmsProperties { get; set; }
}
