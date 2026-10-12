namespace AbpAdmin.Collection.Contracts;

/// <summary>
/// 收款模块错误码。订单模块把它映射成用户能读的句子（计划第 4.2 节）。
/// 码值用 "AbpAdmin.Collection:" 前缀，与框架错误码空间隔离。
/// </summary>
public static class CollectionErrorCodes
{
    public const string Namespace = "AbpAdmin.Collection";

    /// <summary>金额字符串不是合法的十进制数（金额一律十进制字符串，18 位小数以内）。</summary>
    public const string InvalidAmount = Namespace + ":InvalidAmount";

    /// <summary>同一业务单已有一笔 Pending 收款，且渠道、金额或币种与本次请求不同。调用方先取消。</summary>
    public const string PendingCollectionConflict = Namespace + ":PendingCollectionConflict";

    /// <summary>单笔收款金额超过该渠道、该币种的配置上限（与 X_min 各管一头）。</summary>
    public const string AmountExceedsChannelLimit = Namespace + ":AmountExceedsChannelLimit";

    /// <summary>该渠道没有可用网关实现（未配置/未开通）。</summary>
    public const string ChannelNotConfigured = Namespace + ":ChannelNotConfigured";

    /// <summary>Stripe 会话有效期必须在创建后 30 分钟到 24 小时之间；剩余时间不足 30 分钟时直接拒绝。</summary>
    public const string StripeExpiryTooShort = Namespace + ":StripeExpiryTooShort";

    /// <summary>收款单不存在。</summary>
    public const string CollectionNotFound = Namespace + ":CollectionNotFound";

    /// <summary>余额不足。</summary>
    public const string InsufficientBalance = Namespace + ":InsufficientBalance";

    /// <summary>同一业务单已有未回冲扣款，且金额或币种与本次不同。</summary>
    public const string DebitConflict = Namespace + ":DebitConflict";

    /// <summary>充值地址未开放（扫链尚未成功跑过一轮时拒绝新派生）。</summary>
    public const string DepositNotOpen = Namespace + ":DepositNotOpen";

    /// <summary>渠道调用超时或结果未知，收款单保持 Pending 并标记未知。调用方稍后重试或等补单作业。</summary>
    public const string ChannelOutcomeUnknown = Namespace + ":ChannelOutcomeUnknown";

    /// <summary>渠道明确失败（业务拒绝），收款单已改为 ChannelFailed。调用方可重试。</summary>
    public const string ChannelFailed = Namespace + ":ChannelFailed";

    /// <summary>这两套表没有租户列：租户身份调用用户侧余额接口直接拒绝。</summary>
    public const string HostOnly = Namespace + ":HostOnly";

    /// <summary>地址派生锁被占（计划第 5.6 节：等待 5 秒拿不到就请重试）。</summary>
    public const string TryAgain = Namespace + ":TryAgain";

    /// <summary>重置助记词被拒（已有充值地址或未完成归集）。</summary>
    public const string MnemonicResetRefused = Namespace + ":MnemonicResetRefused";

    /// <summary>链网运维动作缺少 Collection:MasterKey 配置。</summary>
    public const string MasterKeyMissing = Namespace + ":MasterKeyMissing";

    /// <summary>RPC 地址非法（仅允许 https）。</summary>
    public const string InvalidRpcUrl = Namespace + ":InvalidRpcUrl";

    /// <summary>金库地址非法（需 0x + 40 位十六进制）。</summary>
    public const string InvalidVaultAddress = Namespace + ":InvalidVaultAddress";

    /// <summary>扫链响应上限数值非法（需 ≥100）。</summary>
    public const string InvalidCap = Namespace + ":InvalidCap";
}
