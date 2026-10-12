namespace AbpAdmin.Collection;

/// <summary>
/// 收款模块常量：表前缀 Col / 连接串名 / 迁移记账表（改名清单同 Biz.Template 约定）。
/// </summary>
public static class CollectionConsts
{
    public const string DbTablePrefix = "Col";

    /// <summary>与框架同库（同库多上下文，Default 连接串）——两个 DbContext 都必须是 Default，
    /// 否则 ABP 不会让它们共享同一个数据库事务（架构测试钉住）。</summary>
    public const string ConnectionStringName = "Default";

    /// <summary>本模块 EF Core 迁移记账表，与框架 __EFMigrationsHistory 两本账。</summary>
    public const string SchemaHistoryTable = "__AbpAdminCollection_EFMigrationsHistory";

    public const int MaxCollectionNoLength = 64;

    public const int MaxBusinessTypeLength = 64;

    public const int MaxTradeNoLength = 128;

    public const int MaxPayloadLength = 2048;

    public const int MaxNoteLength = 512;

    // ── 链上（M5 计划第 59、289 节）───────────────────────────────────────────────────

    /// <summary>一期唯一的链网络名（种子建行；用户地址/充值按它解析）。</summary>
    public const string BscNetworkName = "BSC";

    public const string ScannerLockKey = "Collection:Scanner";

    public const string SweepLockKey = "Collection:Sweep";

    /// <summary>
    /// 助记词生成/重置与用户地址派生共用的全局锁键：堵住 check-then-write 竞态——
    /// 重置检查后、写库前有用户派生提交 → 覆写密文会让该用户地址永远不可达（评审 H 级）。
    /// 派生频率低（每用户一次），全局串行可接受。
    /// </summary>
    public const string MnemonicLockKey = "Collection:Mnemonic";

    /// <summary>
    /// 三类固定前缀告警（计划第 5.6 节：上线前日志采集按前缀告警，或值班用户收到站内信）：
    /// Gas 地址 BNB 过低、归集卡住、代币暂停。
    /// </summary>
    public const string AlertPrefixGasLow = "[Alert:GasLow] ";

    public const string AlertPrefixSweepStuck = "[Alert:SweepStuck] ";

    public const string AlertPrefixTokenPaused = "[Alert:TokenPaused] ";
}
