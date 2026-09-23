using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Volo.Abp.AuditLogging;

namespace AbpAdmin.AuditLogs;

/// <summary>
/// 「已处理」标记列（AuditLog 的 Entity Extensions 映射列，EF 影子属性）的查询端缝隙。
/// 列只能在 EF 表达式里经 EF.Property 引用（ABP 的 x.GetProperty 是普通静态方法，
/// EF 翻译不了），而 Application 层刻意不引 EF Core（查询一律 AsyncExecuter 保持
/// provider 无关），故这一小段 EF 特有表达式收进接口，由 EntityFrameworkCore
/// 项目实现、宿主组装。读写分工：
/// 写经实体 SetProperty（SaveChanges 时 ABP 自动字典→列同步）；
/// 筛选经 <see cref="ApplyUnhandled"/>（单表 HandledAt IS NULL）；
/// 读经 <see cref="GetStatesAsync"/>（列投影）——不能用 GetProperty 读：模块仓储的
/// GetListAsync 是 AsNoTracking，影子列不触发 ABP 的 track 回填，字典恒为空
/// （实测 JSON 双写也不兜底）。
/// </summary>
public interface IAuditLogHandledColumnQueries
{
    /// <summary>向查询追加「未处理」条件（HandledAt IS NULL）。</summary>
    IQueryable<AuditLog> ApplyUnhandled(IQueryable<AuditLog> queryable);

    /// <summary>按 AuditLog Id 批量投影处理状态（列表页/详情防 N+1：一次查询）。</summary>
    Task<List<AuditLogHandledState>> GetStatesAsync(IReadOnlyCollection<Guid> auditLogIds);
}

/// <summary>一行审计日志的处理状态投影（来自 AbpAuditLogs 行内映射列）。</summary>
public class AuditLogHandledState
{
    /// <summary>对应的 AuditLog 主键。</summary>
    public Guid AuditLogId { get; init; }

    /// <summary>处理时间；null = 未处理（未处理筛选与 IsHandled 的同一锚点）。</summary>
    public DateTime? HandledAt { get; init; }

    /// <summary>处理人用户 Id。</summary>
    public Guid? HandledByUserId { get; init; }

    /// <summary>处理人用户名快照（展示用）。</summary>
    public string? HandledByName { get; init; }

    /// <summary>处置备注（结论/根因/工单号等，可选）。</summary>
    public string? HandledNote { get; init; }

    /// <summary>未处理锚点唯一出处：HandledAt 非空即已处理（与 ApplyUnhandled 同列同口径）。</summary>
    public bool IsHandled => HandledAt != null;
}
