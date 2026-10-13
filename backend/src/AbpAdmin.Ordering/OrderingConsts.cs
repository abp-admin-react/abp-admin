namespace AbpAdmin.Ordering;

/// <summary>
/// 订单模块常量：表前缀 Ord / 连接串名 / 迁移记账表。
/// </summary>
public static class OrderingConsts
{
    public const string DbTablePrefix = "Ord";

    /// <summary>与框架同库（同库多上下文，Default 连接串）——必须是 Default，
    /// 否则与收款上下文共享事务静默消失（架构测试钉住，计划第 2 节）。</summary>
    public const string ConnectionStringName = "Default";

    /// <summary>本模块 EF Core 迁移记账表，与框架 __EFMigrationsHistory 两本账。</summary>
    public const string SchemaHistoryTable = "__AbpAdminOrdering_EFMigrationsHistory";

    public const int MaxOrderNoLength = 64;

    public const int MaxSubjectLength = 256;

    public const int MaxNoteLength = 512;

    public const int MaxTradeNoLength = 128;

    /// <summary>
    /// 订单模块的业务单类型（传给收款集成服务的 BusinessType）。
    /// 收款不解释其含义（计划第 10 节）。
    /// </summary>
    public const string OrderBusinessType = "Order";

    /// <summary>当前唯一支付网络名（与 ColNetwork.Name 种子一致）：余额/充值订单 PayNetwork 的取值。
    /// 多链上线时由渠道定义携带网络名，逐步替换此单链常量的引用点。</summary>
    public const string BscNetworkName = "BSC";
}
