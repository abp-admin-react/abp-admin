using System;

namespace AbpAdmin.Collection.Contracts;

/// <summary>
/// 微信、支付宝或 Stripe 到账（计划第 3.2 节）。收款模块在本库事务提交后发布。
/// 链上充值不产生本事件（链上走 <see cref="BalanceCreditedEto"/>）。
/// 金额一律十进制字符串。
/// </summary>
[Serializable]
public class CollectionCompletedEto
{
    public Guid CollectionId { get; set; }

    public string CollectionNo { get; set; } = default!;

    public string BusinessType { get; set; } = default!;

    public Guid BusinessId { get; set; }

    public Guid UserId { get; set; }

    public CollectionChannel Channel { get; set; }

    public CollectionCurrency Currency { get; set; }

    public string Amount { get; set; } = default!;

    public DateTime CompletedAt { get; set; }

    /// <summary>渠道单号（微信/支付宝交易号、Stripe PaymentIntent/Session id）。</summary>
    public string? ChannelTradeNo { get; set; }
}

/// <summary>
/// 常驻地址归集确认并已写入余额（计划第 3.2 节）。订单模块插入已支付的充值订单，
/// BusinessId 等于 BalanceTxnId 且唯一。
/// </summary>
[Serializable]
public class BalanceCreditedEto
{
    /// <summary>
    /// 固定 <see cref="CollectionBusinessTypes.TopUp"/>。可设置属性而不是计算属性：
    /// 事件总线远程化后要经 JSON 往返，get-only 字段在消费端会丢（发布方负责写值）。
    /// </summary>
    public string BusinessType { get; set; } = CollectionBusinessTypes.TopUp;

    /// <summary>等于 BalanceTxnId（发布方负责写值）。</summary>
    public Guid BusinessId { get; set; }

    public Guid BalanceTxnId { get; set; }

    public Guid UserId { get; set; }

    public CollectionCurrency Currency { get; set; }

    public string Amount { get; set; } = default!;

    public DateTime CompletedAt { get; set; }
}

/// <summary>
/// 扣款成功且与标支付在同一工作单元提交之后发布（计划第 3.2 节）。
/// 订单模块收到时订单应已是 Paid；仍是 PendingPayment 说明扣款没和标支付放在同一个
/// 工作单元里，只记异常，交给订单侧补状态作业。
/// </summary>
[Serializable]
public class BalanceDebitedEto
{
    public string BusinessType { get; set; } = default!;

    public Guid BusinessId { get; set; }

    public Guid BalanceTxnId { get; set; }

    public Guid UserId { get; set; }

    public CollectionCurrency Currency { get; set; }

    public string Amount { get; set; } = default!;

    public DateTime CompletedAt { get; set; }
}
