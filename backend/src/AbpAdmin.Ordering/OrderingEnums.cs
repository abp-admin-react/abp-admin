namespace AbpAdmin.Ordering;

/// <summary>
/// 订单类型。充值订单只在 <c>BalanceCreditedEto</c> 或补状态作业里插入，停在 Paid，不标 Fulfilled。
/// </summary>
public enum OrderType
{
    TopUp = 1,
    Business = 2,
}

/// <summary>订单状态。本期没有从 Fulfilled 出发的状态转移（计划第 3.2 节）。</summary>
public enum OrderStatus
{
    PendingPayment = 0,
    Paid = 1,
    Closed = 2,
    Fulfilled = 3,
}

/// <summary>关闭原因。只有 Expired 且没有任何异常明细时，才可以被匹配的到账标回 Paid。</summary>
public enum OrderCloseReason
{
    UserCancel = 1,
    Expired = 2,
}

/// <summary>
/// 支付方式。渠道三种与契约的 <see cref="AbpAdmin.Collection.Contracts.CollectionChannel"/> 对齐；
/// 余额按币种拆成两个方式——报价的部分唯一索引是 (OrderId, PayMethod)，USDT 与 USDC 是
/// 各自独立的「支付方式」（各自一套费率配置，计划第 4.2 节表格的「余额」行按币取费率）。
/// 与 PayAmount/PayCurrency 在每次发起支付时一起写入，Paid 之后不改；待支付期间可以换方式
/// （换之前必须先 CancelAsync，计划第 4.1 节）。
/// </summary>
public enum OrderingPayChannel
{
    WechatNative = 1,
    AlipayPrecreate = 2,
    StripeCheckout = 3,
    BalanceUsdt = 4,
    BalanceUsdc = 5,
}

public static class OrderingPayChannelExtensions
{
    /// <summary>余额支付只走 DebitBalanceAsync，没有外部渠道——返回 null。</summary>
    public static AbpAdmin.Collection.Contracts.CollectionChannel? ToCollectionChannel(this OrderingPayChannel channel)
    {
        return channel switch
        {
            OrderingPayChannel.WechatNative => AbpAdmin.Collection.Contracts.CollectionChannel.WechatNative,
            OrderingPayChannel.AlipayPrecreate => AbpAdmin.Collection.Contracts.CollectionChannel.AlipayPrecreate,
            OrderingPayChannel.StripeCheckout => AbpAdmin.Collection.Contracts.CollectionChannel.StripeCheckout,
            _ => null,
        };
    }

    public static bool IsBalance(this OrderingPayChannel channel)
    {
        return channel is OrderingPayChannel.BalanceUsdt or OrderingPayChannel.BalanceUsdc;
    }

    /// <summary>余额方式的币种；非余额方式返回 null（币种由报价决定）。</summary>
    public static AbpAdmin.Collection.Contracts.CollectionCurrency? BalanceCurrency(this OrderingPayChannel channel)
    {
        return channel switch
        {
            OrderingPayChannel.BalanceUsdt => AbpAdmin.Collection.Contracts.CollectionCurrency.Usdt,
            OrderingPayChannel.BalanceUsdc => AbpAdmin.Collection.Contracts.CollectionCurrency.Usdc,
            _ => null,
        };
    }

    /// <summary>渠道完成事件里的渠道映射回支付方式（余额方式不会有 CollectionCompletedEto）。</summary>
    public static OrderingPayChannel FromCollectionChannel(AbpAdmin.Collection.Contracts.CollectionChannel channel)
    {
        return channel switch
        {
            AbpAdmin.Collection.Contracts.CollectionChannel.WechatNative => OrderingPayChannel.WechatNative,
            AbpAdmin.Collection.Contracts.CollectionChannel.AlipayPrecreate => OrderingPayChannel.AlipayPrecreate,
            AbpAdmin.Collection.Contracts.CollectionChannel.StripeCheckout => OrderingPayChannel.StripeCheckout,
            _ => throw new System.ArgumentOutOfRangeException(nameof(channel), channel, null),
        };
    }
}

/// <summary>报价状态。同一订单、同一支付方式、Active 的报价只有一行。</summary>
public enum QuoteStatus
{
    Active = 1,
    Expired = 2,
}
