using System;

namespace AbpAdmin.ClickHouse;

/// <summary>
/// ClickHouse 接入配置,绑定 "ClickHouse" 配置节。
/// ABP 框架没有官方 CH 集成,这里用官方 ClickHouse.Driver 客户端封装。
/// 定位:主库(PG)仍是业务事实记录源,CH 只存采集/分析类数据(可重放的副本),
/// 因此刻意不接入 ABP 的 UnitOfWork/仓储体系。
/// 数值字段(容量/批量/间隔)非法值(非正数)不在此校验,由写入器构造时收敛到安全默认——
/// 单点防御,避免每处使用方重复判空。
/// </summary>
public class AbpAdminClickHouseOptions
{
    /// <summary>配置节名,绑定/读取统一引用此常量(消费方:CH 模块注册、健康检查注册)</summary>
    public const string SectionName = "ClickHouse";

    /// <summary>总开关:false 时不注册客户端/写入器/健康检查,宿主对 CH 零依赖</summary>
    public bool IsEnabled { get; set; }

    /// <summary>完整连接串,如 Host=...;Port=8123;Username=...;Password=...;Database=default(走 HTTP 8123)。
    /// 传输是明文 HTTP + basic auth,内网部署取舍;跨网段/生产外网时应前置 TLS(反代)或启用 CH 的 TLS 端口。</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 配置是否足以启用(开关 + 连接串完整)。启用谓词单头化:
    /// 模块注册/健康检查注册统一以此判定,避免多处复制漂移。
    /// </summary>
    public bool IsUsable => IsEnabled && !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>采集事件表(库.表)。模块启动时 CREATE IF NOT EXISTS,不删改已有数据;写入器构造时做白名单校验</summary>
    public string CollectedEventsTable { get; set; } = "default.collected_events";

    /// <summary>
    /// 内存缓冲容量(CH 长时间不可用时的自保上限),写满后丢弃最旧事件,不反压业务线程。
    /// 预算口径注意:按条数封顶,最坏常驻内存 ≈ 容量 × 平均事件字节数(Payload 无长度上限,
    /// 1KB 级事件 × 10 万条 ≈ 100MB)——放大容量前先按字节口径复核。
    /// </summary>
    public int BufferCapacity { get; set; } = 100_000;

    /// <summary>后台冲刷周期(写入器内每周期把缓冲清空为止,稳态吞吐不受本值约束)</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>单批最大行数(CH MergeTree 偏好大批次;批越大单次 HTTP 摊销越高,停机冲刷的最小完成单位也越大)</summary>
    public int MaxBatchSize { get; set; } = 5_000;
}
