namespace AbpAdmin.Collection.Contracts;

/// <summary>
/// 收款渠道。余额支付不在其中——只走 <see cref="ICollectionIntegrationService.DebitBalanceAsync"/>。
/// </summary>
public enum CollectionChannel
{
    WechatNative = 1,
    AlipayPrecreate = 2,
    StripeCheckout = 3,
}

/// <summary>收款单/订单支付侧的币种。</summary>
public enum CollectionCurrency
{
    Cny = 1,
    Usd = 2,
    Usdt = 3,
    Usdc = 4,
}

/// <summary>收款单本地状态。ChannelFailed 明确失败后退出 Pending 唯一索引，同一渠道可重试。</summary>
public enum CollectionStatus
{
    Pending = 0,
    Completed = 1,
    Closed = 2,
    ChannelFailed = 3,
}

/// <summary>
/// 业务单类型（字符串，避免契约工程反向引用订单模块）。
/// 充值事件的 BusinessType 固定为 TopUp，BusinessId 等于 BalanceTxnId。
/// 订单模块使用自己的常量（OrdOrderBusinessTypes.Order），收款不解释其含义。
/// </summary>
public static class CollectionBusinessTypes
{
    public const string TopUp = "TopUp";

    /// <summary>提现单：余额流水的 BusinessType，BusinessId = 提现单 Id。</summary>
    public const string Withdraw = "Withdraw";
}

/// <summary>
/// 稳定币余额的动作类型（M2 落库，契约先行）。
/// 提现四动作（M1 冻结原语）：Freeze/Unfreeze 在可用与冻结两桶间搬运，
/// Settle 把冻结桶真正出账（审核通过），Refund 广播失败退款加回可用。
/// </summary>
public static class BalanceActions
{
    public const string DepositCredit = "DepositCredit";
    public const string BusinessDebit = "BusinessDebit";
    public const string DebitReverse = "DebitReverse";

    public const string WithdrawFreeze = "WithdrawFreeze";
    public const string WithdrawUnfreeze = "WithdrawUnfreeze";
    public const string WithdrawSettle = "WithdrawSettle";
    public const string WithdrawRefund = "WithdrawRefund";
}
