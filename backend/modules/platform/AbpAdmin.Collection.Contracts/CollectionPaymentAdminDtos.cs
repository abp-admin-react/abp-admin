using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Collection.Contracts;

public class CollectionPaymentListInput : PagedResultRequestDto
{
    [StringLength(128)]
    public string? Filter { get; set; }

    [EnumDataType(typeof(CollectionChannel))]
    public CollectionChannel? Channel { get; set; }

    [EnumDataType(typeof(CollectionStatus))]
    public CollectionStatus? Status { get; set; }
}

/// <summary>管理端收款记录。金额用十进制字符串，渠道支付凭据不暴露在记录查询中。</summary>
public class CollectionPaymentAdminDto : EntityDto<Guid>
{
    public string CollectionNo { get; set; } = default!;
    public string BusinessType { get; set; } = default!;
    public Guid BusinessId { get; set; }
    public Guid UserId { get; set; }
    public string Channel { get; set; } = default!;
    public string Currency { get; set; } = default!;
    public string Amount { get; set; } = default!;
    public string Status { get; set; } = default!;
    public string? StatusNote { get; set; }
    public bool ChannelResultUnknown { get; set; }
    public string? ChannelTradeNo { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime CreationTime { get; set; }
}
