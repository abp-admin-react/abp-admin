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

    /// <summary>扫链 API KEY 格式非法（Ankr 64 位十六进制 / Etherscan 34 位字母数字）。</summary>
    public const string InvalidApiKey = Namespace + ":InvalidApiKey";

    /// <summary>同一服务商下该 KEY 已存在（按 SHA-256 判重）。</summary>
    public const string ApiKeyExists = Namespace + ":ApiKeyExists";

    /// <summary>余额两桶状态与预期不符（解冻/扣冻结时冻结桶不足）：数据完整性异常，需人工核查。</summary>
    public const string BalanceStateConflict = Namespace + ":BalanceStateConflict";

    /// <summary>同一提现单的同一动作重放，但金额/币种/用户与原流水不同。</summary>
    public const string WithdrawActionConflict = Namespace + ":WithdrawActionConflict";

    // —— 提现（M2 审核/状态机；M3 补热钱包/双花门）—————————————————————————————

    /// <summary>提现功能未开启（设置 Collection.Withdraw.Enabled=false，设置页热读）。</summary>
    public const string WithdrawDisabled = Namespace + ":WithdrawDisabled";

    /// <summary>提现总闸拉下（运行时设置，紧急停机用）。</summary>
    public const string WithdrawHalted = Namespace + ":WithdrawHalted";

    /// <summary>提现单不存在。</summary>
    public const string WithdrawNotFound = Namespace + ":WithdrawNotFound";

    /// <summary>只能操作本人的提现单。</summary>
    public const string WithdrawNotOwner = Namespace + ":WithdrawNotOwner";

    /// <summary>收款地址非法（需 0x + 40 位十六进制，且不是全零黑洞地址）。</summary>
    public const string WithdrawInvalidAddress = Namespace + ":WithdrawInvalidAddress";

    /// <summary>低于单笔最小提现额。</summary>
    public const string WithdrawBelowMin = Namespace + ":WithdrawBelowMin";

    /// <summary>超过当日提现总额度。</summary>
    public const string WithdrawDailyAmountExceeded = Namespace + ":WithdrawDailyAmountExceeded";

    /// <summary>超过当日提现笔数限额。</summary>
    public const string WithdrawDailyCountExceeded = Namespace + ":WithdrawDailyCountExceeded";

    /// <summary>状态迁移不合法（CAS 未命中：单子已被并发迁移，刷新后重试）。</summary>
    public const string WithdrawInvalidStatus = Namespace + ":WithdrawInvalidStatus";

    /// <summary>拒绝必须填写原因。</summary>
    public const string WithdrawRemarkRequired = Namespace + ":WithdrawRemarkRequired";

    /// <summary>重试次数已达上限。</summary>
    public const string WithdrawRetryLimitReached = Namespace + ":WithdrawRetryLimitReached";

    /// <summary>提现热钱包未配置（地址/私钥）。</summary>
    public const string HotWalletNotConfigured = Namespace + ":HotWalletNotConfigured";

    /// <summary>热钱包链上余额不足以支付本次提现。</summary>
    public const string HotWalletInsolvent = Namespace + ":HotWalletInsolvent";

    /// <summary>热钱包链上余额低于熔断地板——阻断所有提现审批，需人工处理。</summary>
    public const string WithdrawBreakerOpen = Namespace + ":WithdrawBreakerOpen";

    /// <summary>热钱包 BNB 不足（付不了 gas）。</summary>
    public const string HotWalletGasShort = Namespace + ":HotWalletGasShort";

    /// <summary>热钱包存在待确认出站交易（nonce 卡在 mempool），禁止重播。</summary>
    public const string WithdrawPendingOutbound = Namespace + ":WithdrawPendingOutbound";

    /// <summary>近期已向该地址付过款（双花门），禁止盲目重播。</summary>
    public const string WithdrawRecentPayoutExists = Namespace + ":WithdrawRecentPayoutExists";

    /// <summary>进款单不满足放行条件（非 Confirmed 或已挂归集任务）。</summary>
    public const string DepositNotReleasable = Namespace + ":DepositNotReleasable";

    /// <summary>归集任务不存在。</summary>
    public const string SweepTaskNotFound = Namespace + ":SweepTaskNotFound";

    /// <summary>归集任务当前状态不可重试（仅 Failed 可重试，冷却由自动拉回管、手动重试跳过冷却）。</summary>
    public const string SweepTaskNotRetryable = Namespace + ":SweepTaskNotRetryable";

    /// <summary>热钱包监控流水不存在。</summary>
    public const string HotWalletTxnNotFound = Namespace + ":HotWalletTxnNotFound";

    /// <summary>热钱包流水当前状态不可确认（仅 Anomaly 可「确认已知」）。</summary>
    public const string HotWalletTxnNotAckable = Namespace + ":HotWalletTxnNotAckable";
}
