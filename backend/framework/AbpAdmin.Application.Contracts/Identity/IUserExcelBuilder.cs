using System.Threading;
using System.Threading.Tasks;

namespace AbpAdmin.Identity;

/// <summary>
/// 用户导出 Excel 构建器（同步导出与后台作业共用同一实现，见重构报告问题 2）。
/// 接口放 Contracts：HttpApi 控制器（同步导出）与 Application（异步导出作业）都能引用，
/// 消除原先 UserExportController 中 75 行内联副本。
/// 与 IAuditLogExcelBuilder 的既有差异：不提供 CountAsync——审计侧筛选输入是复杂 DTO，
/// 计数与构建必须同源防漂移故收进接口；用户侧筛选只是一个 string filter，
/// 计数由调用方直接走 IIdentityUserRepository.GetCountAsync(filter)（ABP 原生参数化计数），
/// 无需在接口上再包一层。
/// </summary>
public interface IUserExcelBuilder
{
    /// <summary>
    /// 按筛选条件分页拉取用户并生成 Excel 字节数组。
    /// maxResultCount 是硬上限：同步分支传实际条数（≤ 1000），后台作业分支传允许的最大行数。
    /// maskSensitive：true 时邮箱/手机号列脱敏输出（与响应序列化脱敏同一算法 StringMasker）；
    /// 调用方须在仍持有请求上下文时按 AbpIdentity.Users.Update 判定后传入（后台作业无权限上下文）。
    /// cancellationToken：同步导出传请求中止令牌，客户端取消后服务端停止分页拉取（后台作业传 None）。
    /// </summary>
    Task<byte[]> BuildAsync(string? filter, int maxResultCount, bool maskSensitive, CancellationToken cancellationToken = default);
}
