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
}
