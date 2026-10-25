namespace AbpAdmin.Collection.Settings;

/// <summary>
/// 收款模块运行期参数名。机械上限（单笔收款金额按渠道、币种封顶，计划第 4.2/10 节）
/// 与 X_min 各管一头：下限在保存报价费率时校验（订单模块），上限在创建收款时校验（本模块）。
/// 空值 = 不限；配置了但解析不出的非法值按「限制存在但读不出」拒绝（fail-closed，
/// AssertWithinChannelLimitAsync 的实现口径，防止坏配置静默放开上限）。
/// </summary>
public static class CollectionSettings
{
    public const string Prefix = "Collection";

    /// <summary>本模块设置页一级 Tab 名（SettingUi Group1 元数据值）。</summary>
    public const string SettingUiGroup = Prefix;

    /// <summary>单笔收款上限：渠道 × 币种各一个，值为十进制字符串金额。</summary>
    public static class MaxAmount
    {
        public const string WechatNativeCny = Prefix + ".MaxAmount.WechatNative.Cny";
        public const string AlipayPrecreateCny = Prefix + ".MaxAmount.AlipayPrecreate.Cny";
        public const string StripeCheckoutUsd = Prefix + ".MaxAmount.StripeCheckout.Usd";
    }

    /// <summary>
    /// 链上运行参数（计划第 5.6 节）。开关与阈值进宿主配置（扫链/归集默认关；
    /// NodeLogResultCap 不设缺省——没有实测或文档上限就拒绝打开扫链）；
    /// 密文主密钥 Collection:MasterKey 与 RPC/助记词明文只在宿主配置（secrets），
    /// 库里只存密文。Etherscan V2 密钥同理。
    /// </summary>
    public static class Chain
    {
        public const string ScanEnabled = Prefix + ":Chain:ScanEnabled";
        public const string SweepEnabled = Prefix + ":Chain:SweepEnabled";

        /// <summary>扫链轮间隔（秒）；一轮 = 取锁→扫→释放。默认 20s（吞吐高于 BSC 出块速率留余量）。</summary>
        public const string ScanIntervalSeconds = Prefix + ":Chain:ScanIntervalSeconds";

        /// <summary>归集轮间隔（秒）。默认 60s。</summary>
        public const string SweepIntervalSeconds = Prefix + ":Chain:SweepIntervalSeconds";

        /// <summary>每轮墙钟预算（秒），超时取消并释放锁、告警——Redis 锁会自动续期，挂死进程不能永久占锁。</summary>
        public const string RoundBudgetSeconds = Prefix + ":Chain:RoundBudgetSeconds";

        /// <summary>确认数（计划第 5.6 节：按当时出块与终局说明设定，不写死「至少 12」）。</summary>
        public const string Confirmations = Prefix + ":Chain:Confirmations";

        /// <summary>单轮扫链块区间（测量：p95 块 150 条 × 区间 ≤ 0.8×cap；默认 50）。</summary>
        public const string BlockRange = Prefix + ":Chain:BlockRange";

        /// <summary>归集每轮取出上限（该币十进制金额；同址同币合并后超出上限的进款单留给下一轮）。</summary>
        public const string SweepRoundLimit = Prefix + ":Chain:SweepRoundLimit";

        /// <summary>打油单笔 BNB 上限（超过按仅人工失败，防配置错误烧币）。</summary>
        public const string FuelBnbLimit = Prefix + ":Chain:FuelBnbLimit";

        /// <summary>Gas 地址 BNB 告警阈值。</summary>
        public const string GasBnbAlertThreshold = Prefix + ":Chain:GasBnbAlertThreshold";

        /// <summary>可再试失败的冷却（分钟），看 StatusChangedAt。</summary>
        public const string RetryCooldownMinutes = Prefix + ":Chain:RetryCooldownMinutes";

        /// <summary>广播后无回执的宽限（分钟），超过才允许重签。</summary>
        public const string RebroadcastGraceMinutes = Prefix + ":Chain:RebroadcastGraceMinutes";

        /// <summary>扫链落后过深告警阈值（块）。</summary>
        public const string ScanBehindBlocks = Prefix + ":Chain:ScanBehindBlocks";

        /// <summary>低于最低额跳过比例告警：窗口（分钟）与阈值（0-1，分母 = 该币该窗口进款单数）。</summary>
        public const string SkipRatioWindowMinutes = Prefix + ":Chain:SkipRatioWindowMinutes";
        public const string SkipRatioThreshold = Prefix + ":Chain:SkipRatioThreshold";

        /// <summary>告警收件人（Host 用户 id，逗号分隔；空名单 LogError 不发送）。</summary>
        public const string AlertRecipientUserIds = Prefix + ":Chain:AlertRecipientUserIds";
    }

    /// <summary>余额记账运行期参数（计划第 5.5 节）。</summary>
    public static class Balance
    {
        /// <summary>扣款命令超时（秒）：行锁会留到外层订单事务提交，热点账户宁可失败重试也不无限排队。</summary>
        public const string DebitCommandTimeoutSeconds = Prefix + ".Balance.DebitCommandTimeoutSeconds";
    }

    /// <summary>
    /// 提现功能开关与 worker 节奏（宿主配置，冒号键；与 Chain.* 同层）。默认 Enabled=false——
    /// 上线由运维显式打开；DbMigrator 不注册 worker。
    /// </summary>
    public static class WithdrawConfig
    {
        /// <summary>提现总开关：false 时提交拒绝（WithdrawDisabled）、worker 睡眠不碰锁与链。</summary>
        public const string Enabled = Prefix + ":Withdraw:Enabled";

        /// <summary>广播 worker 轮间隔（秒）。默认 15。</summary>
        public const string BroadcastIntervalSeconds = Prefix + ":Withdraw:BroadcastIntervalSeconds";

        /// <summary>每轮墙钟预算（秒）。默认 120。</summary>
        public const string RoundBudgetSeconds = Prefix + ":Withdraw:RoundBudgetSeconds";
    }

    /// <summary>
    /// 提现运行期参数（ISettingProvider 点键，SettingUi 可改）：限额/费率/总闸/热钱包阈值。
    /// 代码默认值在 <see cref="WithdrawDefaults"/>，设置缺省值与之一致。
    /// </summary>
    public static class Withdraw
    {
        /// <summary>单笔最小提现额（总额口径，含手续费）。默认 10。</summary>
        public const string MinAmount = Prefix + ".Withdraw.MinAmount";

        /// <summary>提现费率（0-1 小数，对总额计；手续费 = 总额×费率，8 位截断）。默认 0.004。</summary>
        public const string FeeRate = Prefix + ".Withdraw.FeeRate";

        /// <summary>单笔提现上限（线上 MaxDailyWithdraw 同语义）：本笔 &gt; 上限即拒；0 或空 = 不限。默认 50000。</summary>
        public const string MaxDailyAmount = Prefix + ".Withdraw.MaxDailyAmount";

        /// <summary>当日提现笔数限额。默认 3。</summary>
        public const string MaxDailyCount = Prefix + ".Withdraw.MaxDailyCount";

        /// <summary>小额自动审阈值：Pending 且总额 ≤ 此值由 worker 自动审批。默认 100。</summary>
        public const string AutoApproveMaxAmount = Prefix + ".Withdraw.AutoApproveMaxAmount";

        /// <summary>提现总闸（true = 紧急停机：拒绝提交与全部审批）。默认 false。</summary>
        public const string Halt = Prefix + ".Withdraw.Halt";

        /// <summary>广播超时（分钟）：Broadcasting 超时进卡单恢复。默认 30。</summary>
        public const string BroadcastTimeoutMinutes = Prefix + ".Withdraw.BroadcastTimeoutMinutes";

        /// <summary>广播重试上限（RetryCount 超过即 BroadcastFailed 终态等人工）。默认 3。</summary>
        public const string MaxRetries = Prefix + ".Withdraw.MaxRetries";

        /// <summary>热钱包 USDT 熔断地板：链上余额低于它则阻断所有审批并告警。默认 1000。</summary>
        public const string HotWalletUsdtFloor = Prefix + ".Withdraw.HotWalletUsdtFloor";

        /// <summary>热钱包最低 BNB（付 gas）。默认 0.005。</summary>
        public const string MinHotWalletBnb = Prefix + ".Withdraw.MinHotWalletBnb";
    }

}

/// <summary>提现运行期参数的代码默认值（设置读不出/解析失败时兜底）。</summary>
public static class WithdrawDefaults
{
    public const decimal MinAmount = 10m;
    public const decimal FeeRate = 0.004m;
    public const decimal MaxDailyAmount = 50000m;
    public const int MaxDailyCount = 3;
    public const decimal AutoApproveMaxAmount = 100m;
    public const int BroadcastTimeoutMinutes = 30;
    public const int MaxRetries = 3;
    public const decimal HotWalletUsdtFloor = 1000m;
    public const decimal MinHotWalletBnb = 0.005m;
}
