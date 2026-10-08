using System;

namespace AbpAdmin.Logging;

/// <summary>
/// 日志保留期（借鉴 Admin.NET LogJob / ruoyi-vue-pro 日志清理 Job——两家都有、基座此前缺）。
/// 键位跟随各日志子系统：<c>OperationLogs:RetentionDays</c> 与 <c>Auditing:RetentionDays</c>，
/// 由 Host 模块从 IConfiguration 逐键绑定。
/// 语义：<see cref="OperationLogRetentionDays"/> / <see cref="AuditLogRetentionDays"/>
/// 缺省/0/负数 = 永不清理（缺省安全：不出厂自动删日志）；正整数 = 保留天数。
/// appsettings.json 出厂给 90 作推荐值，部署侧可覆盖或置 0 关闭。
/// </summary>
public class LogRetentionOptions
{
    /// <summary>操作日志（AppOperationLogs）保留天数。缺省 0 = 永不清理。</summary>
    public int OperationLogRetentionDays { get; set; }

    /// <summary>审计日志（AbpAuditLogs，子表 AbpAuditLogActions/AbpEntityChanges 库级联）保留天数。缺省 0 = 永不清理。</summary>
    public int AuditLogRetentionDays { get; set; }

    /// <summary>单批删除行数上限（防长事务与大结果集物化）。清理器侧钳制到非负——负值按 0 处理（空转不清理）。</summary>
    public int BatchSize { get; set; } = 1000;
}
