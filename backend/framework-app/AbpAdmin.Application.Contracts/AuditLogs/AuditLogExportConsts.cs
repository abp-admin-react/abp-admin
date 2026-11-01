namespace AbpAdmin.AuditLogs;

public static class AuditLogExportConsts
{
    /// <summary>
    /// 同步导出阈值。结果集条数不超过此值时同步返回 Excel 文件，超过则转后台作业。
    /// </summary>
    public const int SyncThreshold = 1000;

    /// <summary>
    /// 后台导出的行数硬上限。审计日志只增不减，无上限的全量物化（含 Exceptions 长文本列）
    /// 会打爆进程内存（OOM），且后台作业无人工盯守。结果集超过该值时截断导出，
    /// 并在完成邮件中注明"仅导出前 N 条"。
    /// </summary>
    public const int AsyncMaxRows = 200_000;

    /// <summary>
    /// ExcelBuilder 分批拉取的批大小。与 SyncThreshold 数值相同但语义不同：
    /// 这只是内存物化的分批步长，不是导出阈值。
    /// </summary>
    public const int ExcelBatchSize = 1000;
}
