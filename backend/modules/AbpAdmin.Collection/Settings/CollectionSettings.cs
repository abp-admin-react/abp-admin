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
    /// 链上运行参数（ISettingProvider 点键，SettingUi 可改、worker 每轮热读——免重启）。
    /// 代码默认值在 <see cref="ChainDefaults"/>，设置缺省值与之一致（settings-single-source）。
    /// 开关默认关：不配置即不跑；开启后 worker 每轮热读（设置页改完下一轮生效）。
    /// </summary>
    public static class Chain
    {
        /// <summary>扫链开关（默认 false：worker 纯睡眠不取锁不碰链）。</summary>
        public const string ScanEnabled = Prefix + ".Chain.ScanEnabled";

        /// <summary>归集开关（默认 false）。</summary>
        public const string SweepEnabled = Prefix + ".Chain.SweepEnabled";

        /// <summary>热钱包逐笔监控开关（默认 false；扫描热钱包地址的代币流水并对账提现单）。</summary>
        public const string HotWalletWatchEnabled = Prefix + ".Chain.HotWalletWatchEnabled";

        /// <summary>扫链轮间隔（秒）；一轮 = 取锁→扫→释放。默认 20s（钳制下限 5s）。</summary>
        public const string ScanIntervalSeconds = Prefix + ".Chain.ScanIntervalSeconds";

        /// <summary>归集轮间隔（秒）。默认 60s。</summary>
        public const string SweepIntervalSeconds = Prefix + ".Chain.SweepIntervalSeconds";

        /// <summary>热钱包监控轮间隔（秒）。默认 300s。</summary>
        public const string HotWalletWatchIntervalSeconds = Prefix + ".Chain.HotWalletWatchIntervalSeconds";

        /// <summary>确认数（BSC 0.75s/块，15 块 ≈ 11 秒 + 深度余量；配置即口径）。</summary>
        public const string Confirmations = Prefix + ".Chain.Confirmations";

        /// <summary>单轮扫链块区间（测量：p95 块 150 条 × 区间 ≤ 0.8×cap）。</summary>
        public const string BlockRange = Prefix + ".Chain.BlockRange";

        /// <summary>扫链落后过深告警阈值（块）。</summary>
        public const string ScanBehindBlocks = Prefix + ".Chain.ScanBehindBlocks";

        /// <summary>归集每轮取出上限（十进制金额；空/0 = 不限；同址同币合并后超出上限的进款单留给下一轮）。</summary>
        public const string SweepRoundLimit = Prefix + ".Chain.SweepRoundLimit";

        /// <summary>打油单笔 BNB 上限（超过按仅人工失败，防配置错误烧币）。</summary>
        public const string FuelBnbLimit = Prefix + ".Chain.FuelBnbLimit";

        /// <summary>Gas 地址 BNB 告警阈值。</summary>
        public const string GasBnbAlertThreshold = Prefix + ".Chain.GasBnbAlertThreshold";

        /// <summary>可再试失败的冷却（分钟），看 StatusChangedAt。</summary>
        public const string RetryCooldownMinutes = Prefix + ".Chain.RetryCooldownMinutes";

        /// <summary>广播后无回执的宽限（分钟），超过才允许重签。</summary>
        public const string RebroadcastGraceMinutes = Prefix + ".Chain.RebroadcastGraceMinutes";

        /// <summary>归集 Sweeping 停留超此时长（分钟）告警。</summary>
        public const string SweepStuckMinutes = Prefix + ".Chain.SweepStuckMinutes";

        /// <summary>低于最低额跳过比例告警：窗口（分钟）。</summary>
        public const string SkipRatioWindowMinutes = Prefix + ".Chain.SkipRatioWindowMinutes";

        /// <summary>低于最低额跳过比例告警：阈值（0-1，分母 = 该币该窗口进款单数）。</summary>
        public const string SkipRatioThreshold = Prefix + ".Chain.SkipRatioThreshold";
    }

    /// <summary>
    /// 留在宿主配置（appsettings/secrets）的基础设施参数（冒号键）：密钥/端点/告警收件人
    /// 不进设置页；墙钟预算是挂死防护阀（防误配 1s 预算让轮次永远做不完），也不热改。
    /// </summary>
    public static class Host
    {
        public const string MasterKey = Prefix + ":MasterKey";

        public const string EtherscanV2Endpoint = Prefix + ":Chain:EtherscanV2Endpoint";

        /// <summary>告警收件人（Host 用户 id，逗号分隔；空名单 LogError 不发送）。</summary>
        public const string AlertRecipientUserIds = Prefix + ":Chain:AlertRecipientUserIds";

        /// <summary>扫链/归集每轮墙钟预算（秒）。</summary>
        public const string ChainRoundBudgetSeconds = Prefix + ":Chain:RoundBudgetSeconds";

        /// <summary>提现广播每轮墙钟预算（秒）。</summary>
        public const string WithdrawRoundBudgetSeconds = Prefix + ":Withdraw:RoundBudgetSeconds";

        /// <summary>
        /// 开关的一次性部署引导（true 时 DbMigrator/宿主种子把对应设置写成开；已显式设置过的不覆盖）：
        /// 首次部署用环境变量打开扫链/归集/提现，之后一律在设置页管理（热改）。
        /// </summary>
        public static class Bootstrap
        {
            public const string ScanEnabled = Prefix + ":Bootstrap:ScanEnabled";
            public const string SweepEnabled = Prefix + ":Bootstrap:SweepEnabled";
            public const string WithdrawEnabled = Prefix + ":Bootstrap:WithdrawEnabled";
            public const string HotWalletWatchEnabled = Prefix + ":Bootstrap:HotWalletWatchEnabled";
        }
    }

    /// <summary>余额记账运行期参数（计划第 5.5 节）。</summary>
    public static class Balance
    {
        /// <summary>扣款命令超时（秒）：行锁会留到外层订单事务提交，热点账户宁可失败重试也不无限排队。</summary>
        public const string DebitCommandTimeoutSeconds = Prefix + ".Balance.DebitCommandTimeoutSeconds";
    }

    /// <summary>
    /// 提现运行期参数（ISettingProvider 点键，SettingUi 可改）：开关/限额/费率/总闸/热钱包阈值/广播节奏。
    /// 代码默认值在 <see cref="WithdrawDefaults"/>，设置缺省值与之一致。
    /// </summary>
    public static class Withdraw
    {
        /// <summary>提现总开关：false 时提交拒绝（WithdrawDisabled）、广播 worker 睡眠不碰锁与链。默认 false。</summary>
        public const string Enabled = Prefix + ".Withdraw.Enabled";

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

        /// <summary>广播 worker 轮间隔（秒）。默认 15。</summary>
        public const string BroadcastIntervalSeconds = Prefix + ".Withdraw.BroadcastIntervalSeconds";

        /// <summary>广播队列每轮取件上限。默认 5。</summary>
        public const string BroadcastBatchSize = Prefix + ".Withdraw.BroadcastBatchSize";

        /// <summary>小额自动审每轮取件上限。默认 20。</summary>
        public const string AutoApproveBatchSize = Prefix + ".Withdraw.AutoApproveBatchSize";
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
    public const int BroadcastIntervalSeconds = 15;
    public const int BroadcastBatchSize = 5;
    public const int AutoApproveBatchSize = 20;
}

/// <summary>
/// 链上 worker 运行参数的代码默认值（设置读不出/解析失败时兜底；设置缺省值与此单源一致）。
/// 开关（Scan/Sweep/HotWalletWatch）无默认值常量——定义里是 "false" 字面量（fail-closed）。
/// </summary>
public static class ChainDefaults
{
    public const int ScanIntervalSeconds = 20;
    public const int SweepIntervalSeconds = 60;
    public const int HotWalletWatchIntervalSeconds = 300;
    public const int Confirmations = 15;
    public const int BlockRange = 50;
    public const int ScanBehindBlocks = 2000;
    public const decimal FuelBnbLimit = 0.005m;
    public const decimal GasBnbAlertThreshold = 0.02m;
    public const int RetryCooldownMinutes = 10;
    public const int RebroadcastGraceMinutes = 30;
    public const int SweepStuckMinutes = 120;
    public const int SkipRatioWindowMinutes = 1440;
    public const decimal SkipRatioThreshold = 0.9m;

    /// <summary>轮间隔热读的钳制下限（秒）：防误配 1s 间隔打爆 RPC/扫链 API。</summary>
    public const int MinIntervalSeconds = 5;
}
