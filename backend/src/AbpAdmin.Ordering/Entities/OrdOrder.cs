using System;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Ordering.Entities;

/// <summary>
/// 订单（OrdOrders）。不实现 ISoftDelete，不用带删除位的审计基类（计划第 2 节）。
/// 无租户列——只给 Host 用户（应用服务入口做 Host 守卫）。
/// PayAmount/PayCurrency/PayChannel 三元组每次发起支付时按当前报价一起写入，Paid 之后不改；
/// 尚未发起支付时 PayAmount 为空，补状态作业不做金额比较（PayAmount 是应付数量，不是已到账数量）。
/// </summary>
public class OrdOrder : BasicAggregateRoot<Guid>
{
    public OrdOrder(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618 // EF 反序列化用；非空字段由迁移与写入路径保证
    protected OrdOrder()
    {
    }
#pragma warning restore CS8618

    public string OrderNo { get; set; } = default!;

    public Guid UserId { get; set; }

    public OrderType OrderType { get; set; }

    public string Subject { get; set; } = default!;

    /// <summary>业务单标价（USD，numeric(18,2)）；充值订单此列为空。</summary>
    public decimal? Amount { get; set; }

    /// <summary>业务单为 USD；充值订单为对应稳定币。</summary>
    public string? Currency { get; set; }

    public OrderStatus Status { get; set; }

    public OrderingPayChannel? PayChannel { get; set; }

    /// <summary>这一次应付的数量（numeric(28,18)；法币在写入前舍入到 0.01，稳定币保留 18 位）。</summary>
    public decimal? PayAmount { get; set; }

    public AbpAdmin.Collection.Contracts.CollectionCurrency? PayCurrency { get; set; }

    /// <summary>标成 Paid 时写入。补状态作业用它限定最近 7 天。</summary>
    public DateTime? PaidAt { get; set; }

    /// <summary>业务单使用的报价。</summary>
    public Guid? QuoteId { get; set; }

    /// <summary>当前这一笔外部收款（致付归因）。换渠道前旧收款必须已取消。</summary>
    public Guid? CollectionId { get; set; }

    /// <summary>扣款或充值流水（致付归因）。充值订单此列唯一。</summary>
    public Guid? BalanceTxnId { get; set; }

    /// <summary>待支付截止时间。</summary>
    public DateTime? ExpiresAt { get; set; }

    public OrderCloseReason? CloseReason { get; set; }

    /// <summary>可空标记。有异常时写入一次，不覆盖；明细在 OrdPaymentAnomalyLines（只追加）。</summary>
    public bool PaymentAnomaly { get; set; }

    /// <summary>致付收款的渠道单号（微信/支付宝交易号、Stripe PaymentIntent/Session id）。
    /// 所有渠道版标 Paid 的路径都随归因一起写入（含过期关闭复活）；余额支付的单此列为空。
    /// 用户对账凭证。</summary>
    public string? ChannelTradeNo { get; set; }

    /// <summary>标成 Fulfilled 时写入（履约时间；状态时间线用）。</summary>
    public DateTime? FulfilledAt { get; set; }

    /// <summary>标成 Closed 时写入（用户取消 / 过期关闭）。时间线「关闭」节点的时间口径——
    /// ExpiresAt 只是付款截止，不是实际关闭时刻。</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>
    /// 人工退款累计金额（币种 = 订单标价币种 Currency，两位小数；
    /// RefundedAmount = Σ 退款明细金额——币种与两位小数由 RecordManualRefund 入口校验保证）。
    /// 用户侧可见。</summary>
    public decimal? RefundedAmount { get; set; }

    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 报价（OrdQuotes）。插入新报价的同一事务里，先把该订单该方式已过 ExpiresAt 的 Active 行
/// 标成过期，再插入（部分唯一索引 (OrderId, PayMethod) WHERE Status = 1；PostgreSQL 部分索引
/// 条件里不能写 now()，所以过期靠写入时翻状态 + 作业扫漏网，计划第 4.2 节）。
/// </summary>
public class OrdQuote : BasicAggregateRoot<Guid>
{
    public OrdQuote(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected OrdQuote()
    {
    }
#pragma warning restore CS8618

    public Guid OrderId { get; set; }

    public OrderingPayChannel PayMethod { get; set; }

    public AbpAdmin.Collection.Contracts.CollectionCurrency PayCurrency { get; set; }

    /// <summary>应付数量（numeric(28,18)；法币已按公式 Ceil 到 0.01）。</summary>
    public decimal PayAmount { get; set; }

    /// <summary>本次报价使用的美元/人民币汇率快照（审计与排障用；设置项后改不影响已生成报价）。</summary>
    public decimal UsdCnyRate { get; set; }

    public QuoteStatus Status { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreationTime { get; set; }
}

/// <summary>
/// 支付异常明细（OrdPaymentAnomalyLines）：时间、金额、币种、渠道单号、说明。只追加，不更新；
/// 人工退款也记在明细上（不改订单 Status）。订单的 PaymentAnomaly 标记在第一条明细写入时置位一次。
/// </summary>
public class OrdPaymentAnomalyLine : Entity<Guid>
{
    public OrdPaymentAnomalyLine(Guid id)
        : base(id)
    {
    }

#pragma warning disable CS8618
    protected OrdPaymentAnomalyLine()
    {
    }
#pragma warning restore CS8618

    public Guid OrderId { get; set; }

    public decimal? Amount { get; set; }

    public AbpAdmin.Collection.Contracts.CollectionCurrency? Currency { get; set; }

    public Guid? CollectionId { get; set; }

    public string? ChannelTradeNo { get; set; }

    public string Note { get; set; } = default!;

    public DateTime CreationTime { get; set; }
}
