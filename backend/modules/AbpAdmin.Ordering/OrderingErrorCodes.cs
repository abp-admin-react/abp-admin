namespace AbpAdmin.Ordering;

/// <summary>
/// 订单模块错误码（单工程，无独立 Domain.Shared——计划第 2 节，常量与错误码放本工程静态类）。
/// </summary>
public static class OrderingErrorCodes
{
    public const string Namespace = "AbpAdmin.Ordering";

    /// <summary>订单不存在。</summary>
    public const string OrderNotFound = Namespace + ":OrderNotFound";

    /// <summary>当前订单状态不允许该操作。</summary>
    public const string InvalidOrderStatus = Namespace + ":InvalidOrderStatus";

    /// <summary>只有业务单可以标记履约（充值订单停在 Paid）。</summary>
    public const string NotAFulfillableOrder = Namespace + ":NotAFulfillableOrder";

    /// <summary>该支付方式不可用（如 Stripe 报价剩余不足 30 分钟、余额记账未开放）。</summary>
    public const string PayMethodUnavailable = Namespace + ":PayMethodUnavailable";

    /// <summary>金额低于该支付方式的下限（X_min）。</summary>
    public const string BelowMinimumAmount = Namespace + ":BelowMinimumAmount";

    /// <summary>报价参数非法（分母小于等于 0 等）。</summary>
    public const string InvalidQuoteConfig = Namespace + ":InvalidQuoteConfig";

    /// <summary>订单输入非法（标题缺失/超长、金额越界等）。</summary>
    public const string InvalidOrderInput = Namespace + ":InvalidOrderInput";

    /// <summary>只有 Host 用户可以使用订单/收款功能（这两套表没有租户列）。</summary>
    public const string HostOnly = Namespace + ":HostOnly";
}
