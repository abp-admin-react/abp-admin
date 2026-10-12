using System;
using System.Collections.Generic;

namespace AbpAdmin.Ordering.Services.Dtos;

/// <summary>订单 DTO。金额字段是十进制字符串（前端不解析 JSON 数字，镜像 web/src/abp/ordering.ts）。</summary>
public class OrderDto
{
    public Guid Id { get; set; }

    public string OrderNo { get; set; } = default!;

    public Guid UserId { get; set; }

    public string OrderType { get; set; } = default!;

    public string Subject { get; set; } = default!;

    /// <summary>业务单标价（USD，两位小数字符串）；充值订单为 null。</summary>
    public string? Amount { get; set; }

    public string? Currency { get; set; }

    public string Status { get; set; } = default!;

    public string? PayChannel { get; set; }

    /// <summary>这一次应付的数量（最多 18 位小数字符串）；尚未发起支付时为 null。</summary>
    public string? PayAmount { get; set; }

    public string? PayCurrency { get; set; }

    public DateTime? PaidAt { get; set; }

    public Guid? QuoteId { get; set; }

    public Guid? CollectionId { get; set; }

    public Guid? BalanceTxnId { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public string? CloseReason { get; set; }

    public bool PaymentAnomaly { get; set; }

    public DateTime CreationTime { get; set; }

    public List<OrderAnomalyLineDto>? AnomalyLines { get; set; }
}

public class OrderAnomalyLineDto
{
    public Guid Id { get; set; }

    public string? Amount { get; set; }

    public string? Currency { get; set; }

    public Guid? CollectionId { get; set; }

    public string? ChannelTradeNo { get; set; }

    public string Note { get; set; } = default!;

    public DateTime CreationTime { get; set; }
}

public class CreateOrderInput
{
    public string Subject { get; set; } = default!;

    /// <summary>业务单标价（USD，两位小数字符串）。</summary>
    public string Amount { get; set; } = default!;
}

public class OrderListFilterDto
{
    /// <summary>TopUp / Business；空 = 全部。</summary>
    public string? OrderType { get; set; }

    /// <summary>PendingPayment / Paid / Closed / Fulfilled；空 = 全部。</summary>
    public string? Status { get; set; }

    /// <summary>只看有异常明细的订单（管理端「有异常」预置过滤）。</summary>
    public bool? HasAnomaly { get; set; }

    /// <summary>只看该用户的订单（管理端按用户排查）。</summary>
    public Guid? UserId { get; set; }

    /// <summary>分页偏移；负数按 0 处理（与 OrderServiceHelpers.NormalizePaging 共同钳制）。</summary>
    public int SkipCount { get; set; }

    /// <summary>页大小；&lt;=0 用端点各自缺省，超上限钳到上限（用户端 100 / 管理端 500）。</summary>
    public int MaxResultCount { get; set; }
}

public class QuoteDto
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public string PayMethod { get; set; } = default!;

    public string PayCurrency { get; set; } = default!;

    /// <summary>应付数量（最多 18 位小数字符串）。</summary>
    public string PayAmount { get; set; } = default!;

    public string UsdCnyRate { get; set; } = default!;

    public string Status { get; set; } = default!;

    public DateTime ExpiresAt { get; set; }
}

/// <summary>发起支付的结果：渠道单返回二维码/跳转凭据；余额返回已支付。</summary>
public class StartPayResultDto
{
    public string Outcome { get; set; } = default!;

    public OrderDto Order { get; set; } = default!;

    /// <summary>渠道凭据：微信 code_url、支付宝 qr_code、Stripe Checkout 跳转地址。</summary>
    public string? ChannelPayload { get; set; }

    public Guid? CollectionId { get; set; }
}

public class RecordManualRefundInput
{
    /// <summary>退款金额（十进制字符串；null = 未指明金额的说明性记录）。</summary>
    public string? Amount { get; set; }

    public string? Currency { get; set; }

    public string Note { get; set; } = default!;
}
