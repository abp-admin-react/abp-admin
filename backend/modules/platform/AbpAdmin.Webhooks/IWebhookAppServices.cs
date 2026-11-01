using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Webhooks;

/// <summary>Webhook 订阅管理（Auto API：/api/app/webhook-subscription）。</summary>
public interface IWebhookSubscriptionAppService : IApplicationService
{
    Task<PagedResultDto<WebhookSubscriptionDto>> GetListAsync(GetWebhookSubscriptionListInput input);

    Task<WebhookSubscriptionDto> GetAsync(Guid id);

    Task<WebhookSubscriptionDto> CreateAsync(CreateWebhookSubscriptionInput input);

    Task<WebhookSubscriptionDto> UpdateAsync(Guid id, UpdateWebhookSubscriptionInput input);

    Task DeleteAsync(Guid id);
}

public class WebhookSubscriptionDto : AuditedEntityDto<Guid>
{
    public string WebhookUri { get; set; } = default!;

    /// <summary>密钥不回传（只回传是否已设置）。</summary>
    public bool HasSecret { get; set; }

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public List<string> Events { get; set; } = new();
}

public class GetWebhookSubscriptionListInput : PagedResultRequestDto
{
    /// <summary>按 URI/描述模糊过滤（可选）。</summary>
    public string? Filter { get; set; }

    /// <summary>按状态精确过滤（可选；null = 全部）。</summary>
    public bool? IsActive { get; set; }
}

public class CreateWebhookSubscriptionInput
{
    [Required]
    [StringLength(512)]
    public string WebhookUri { get; set; } = default!;

    [Required]
    [StringLength(256)]
    public string Secret { get; set; } = default!;

    [StringLength(256)]
    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    [Required]
    [MinLength(1)]
    public List<string> Events { get; set; } = new();
}

public class UpdateWebhookSubscriptionInput
{
    [Required]
    [StringLength(512)]
    public string WebhookUri { get; set; } = default!;

    /// <summary>可选：null/空白 = 保持原密钥不变（编辑表单不回显密钥）。</summary>
    [StringLength(256)]
    public string? Secret { get; set; }

    [StringLength(256)]
    public string? Description { get; set; }

    public bool IsActive { get; set; }

    [Required]
    [MinLength(1)]
    public List<string> Events { get; set; } = new();
}

/// <summary>Webhook 发送记录（Auto API：/api/app/webhook-send-record）。</summary>
public interface IWebhookSendRecordAppService : IApplicationService
{
    Task<PagedResultDto<WebhookSendRecordDto>> GetListAsync(GetWebhookSendRecordListInput input);

    Task DeleteAsync(Guid id);

    /// <summary>重发：以记录中保存的负载重新入队交付作业（不修改记录本身；新结果覆盖终态）。</summary>
    Task ResendAsync(Guid id);
}

public class WebhookSendRecordDto : AuditedEntityDto<Guid>
{
    public Guid SubscriptionId { get; set; }

    public string EventName { get; set; } = default!;

    public string Payload { get; set; } = default!;

    public bool Succeeded { get; set; }

    public int? ResponseStatusCode { get; set; }

    public string? ResponseBody { get; set; }

    public int AttemptCount { get; set; }

    public DateTime LastAttemptTime { get; set; }
}

public class GetWebhookSendRecordListInput : PagedResultRequestDto
{
    /// <summary>按订阅过滤（可选）。</summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>按事件名过滤（可选）。</summary>
    public string? EventName { get; set; }

    /// <summary>按结果过滤（可选；null = 全部）。</summary>
    public bool? Succeeded { get; set; }
}
