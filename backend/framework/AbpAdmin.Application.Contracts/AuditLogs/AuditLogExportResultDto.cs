namespace AbpAdmin.AuditLogs;

/// <summary>
/// 异步导出结果。同步导出请走 Controller 的 GET /api/app/audit-log/export，直接返回文件流。
/// </summary>
public class AuditLogExportResultDto
{
    /// <summary>
    /// 是否已转为后台任务。true 时前端应提示用户等待邮件通知。
    /// </summary>
    public bool IsQueued { get; set; }

    public static AuditLogExportResultDto Queued()
    {
        return new AuditLogExportResultDto
        {
            IsQueued = true
        };
    }
}
