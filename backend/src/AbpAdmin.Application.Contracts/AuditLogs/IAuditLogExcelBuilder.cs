using System.Threading.Tasks;

namespace AbpAdmin.AuditLogs;

/// <summary>
/// 审计日志导出 Excel 构建器（同步导出与后台作业共用同一实现）。
/// 接口放 Contracts（与 Identity 的 IUserExcelBuilder 同一先例）：
/// HttpApi 控制器（同步导出）与 Application（异步导出作业）都能引用，
/// HttpApi 项目因此不再需要引用 AbpAdmin.Application，恢复 ABP 分层依赖方向。
/// </summary>
public interface IAuditLogExcelBuilder
{
    /// <summary>
    /// 按筛选条件统计审计日志条数（同步导出阈值判断用）。
    /// 与 <see cref="BuildAsync"/> 同源同筛选语义，避免控制器自行拼查询条件造成两处漂移。
    /// </summary>
    Task<long> CountAsync(GetAuditLogListInput input);

    /// <summary>
    /// 按筛选条件分批拉取审计日志并生成 Excel 字节数组。
    /// maxResultCount 是硬上限：同步分支传实际条数（≤ <see cref="AuditLogExportConsts.SyncThreshold"/>），
    /// 后台作业分支传 <see cref="AuditLogExportConsts.AsyncMaxRows"/>（调用方负责截断语义与邮件说明）。
    /// </summary>
    Task<byte[]> BuildAsync(GetAuditLogListInput input, int maxResultCount);
}
