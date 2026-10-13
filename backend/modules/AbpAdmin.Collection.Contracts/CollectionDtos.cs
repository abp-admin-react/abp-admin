using System;
using System.Collections.Generic;

namespace AbpAdmin.Collection.Contracts;

/// <summary>收款单对外形状（按收款号查询/列表/创建返回共用）。</summary>
public class CollectionDto
{
    public Guid Id { get; set; }

    public string CollectionNo { get; set; } = default!;

    public string BusinessType { get; set; } = default!;

    public Guid BusinessId { get; set; }

    public Guid UserId { get; set; }

    public CollectionChannel Channel { get; set; }

    public CollectionCurrency Currency { get; set; }

    /// <summary>十进制字符串（不超过 18 位小数）。</summary>
    public string Amount { get; set; } = default!;

    public CollectionStatus Status { get; set; }

    /// <summary>渠道明确失败/结果未知时的说明（给补单与排障看）。</summary>
    public string? StatusNote { get; set; }

    /// <summary>渠道结果未知标记（超时/网络失败，保持 Pending 由作业按商户订单号查询）。</summary>
    public bool ChannelResultUnknown { get; set; }

    public DateTime? ExpiresAt { get; set; }

    /// <summary>渠道侧凭据：微信 code_url、支付宝 qr_code、Stripe Checkout 跳转地址。</summary>
    public string? ChannelPayload { get; set; }

    public string? ChannelTradeNo { get; set; }

    public DateTime? CompletedAt { get; set; }
}

/// <summary>创建一笔外部收款（计划第 3.1 节）。</summary>
public class CreateCollectionInput
{
    public string BusinessType { get; set; } = default!;

    public Guid BusinessId { get; set; }

    public Guid UserId { get; set; }

    public CollectionChannel Channel { get; set; }

    public CollectionCurrency Currency { get; set; }

    /// <summary>十进制字符串；由订单模块按报价算好传入，收款模块只做格式与上限校验。</summary>
    public string Amount { get; set; } = default!;

    /// <summary>不得晚于报价过期；创建时同一个 ExpiresAt 下发渠道（time_expire / expires_at）。</summary>
    public DateTime ExpiresAt { get; set; }
}

/// <summary>取消仍在等待的收款（计划第 3.1 节）。返回取消后的当前状态，不抛状态异常。</summary>
public class CancelCollectionResult
{
    /// <summary>取消动作之后的收款单最新状态（可能是 Completed——调用方按金额币种处理）。</summary>
    public CollectionDto Collection { get; set; } = default!;
}

public class DebitBalanceInput
{
    public string BusinessType { get; set; } = default!;

    public Guid BusinessId { get; set; }

    public Guid UserId { get; set; }

    /// <summary>只允许 USDT / USDC。</summary>
    public CollectionCurrency Currency { get; set; }

    /// <summary>十进制字符串。</summary>
    public string Amount { get; set; } = default!;
}

public class DebitBalanceResult
{
    public Guid BalanceTxnId { get; set; }
}

/// <summary>按业务单号读取未回冲扣款；没有则返回 null（补状态作业判断「已扣未标支付」）。</summary>
public class DebitDto
{
    public Guid BalanceTxnId { get; set; }

    public string BusinessType { get; set; } = default!;

    public Guid BusinessId { get; set; }

    public Guid UserId { get; set; }

    public CollectionCurrency Currency { get; set; }

    /// <summary>十进制字符串。</summary>
    public string Amount { get; set; } = default!;
}

/// <summary>已入账的充值流水（单调序号供 <see cref="ICollectionIntegrationService.ListCreditedTxnsAsync"/>）。</summary>
public class CreditedTxnDto
{
    public long Seq { get; set; }

    public Guid BalanceTxnId { get; set; }

    public Guid UserId { get; set; }

    public CollectionCurrency Currency { get; set; }

    /// <summary>十进制字符串。</summary>
    public string Amount { get; set; } = default!;

    public DateTime CreditedAt { get; set; }
}

public class ListCreditedTxnsResult
{
    public List<CreditedTxnDto> Items { get; set; } = [];

    /// <summary>本批最大序号（含被跳过的行）；空批时等于调用方传入的游标。</summary>
    public long Cursor { get; set; }
}

/// <summary>返回该用户的 BSC 地址。有副作用（可能派生新地址），不是纯查询。</summary>
public class DepositAddressDto
{
    public Guid UserId { get; set; }

    public string Address { get; set; } = default!;

    /// <summary>充值是否开放（扫链尚未成功跑过一轮时为 false）。</summary>
    public bool DepositEnabled { get; set; }

    /// <summary>DepositEnabled 为 false 时的用户可读说明。</summary>
    public string? Notice { get; set; }
}
