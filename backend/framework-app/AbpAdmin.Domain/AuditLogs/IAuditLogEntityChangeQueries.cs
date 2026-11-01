using System;
using System.Threading.Tasks;
using Volo.Abp.AuditLogging;

namespace AbpAdmin.AuditLogs;

/// <summary>
/// 实体变更记录的查询端缝隙。EntityChange 是 AuditLog 聚合内的子实体（非聚合根），
/// ABP 未注册独立仓储（<c>IRepository&lt;EntityChange, Guid&gt;</c> 不可解析，且模块提供的
/// GetEntityChangeListAsync 只能按实体维度翻页），而实体回滚需要"按变更 Id 精确取单条
/// 含属性明细"的读取。与 <see cref="IAuditLogHandledColumnQueries"/> 同一收口方式：
/// 接口在 Domain，EF 实现在 EntityFrameworkCore 项目，保持 Application 层 provider 无关。
/// </summary>
public interface IAuditLogEntityChangeQueries
{
    /// <summary>按变更 Id 取实体变更记录（含 PropertyChanges 明细）；不存在返回 null。</summary>
    Task<EntityChange?> FindWithPropertiesAsync(Guid entityChangeId);
}
