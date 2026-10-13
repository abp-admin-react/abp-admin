using System;
using System.Threading;
using System.Threading.Tasks;

namespace AbpAdmin.ScheduledJobs;

/// <summary>
/// 定时作业处理器抽象（T3.3 第 6 步）。实现一律 ITransientDependency，
/// 放在各自的功能目录下（跟着业务代码走，不集中放 ScheduledJobs/Handlers/）。
/// 每次触发都会在本次执行的 scope 里解析一个新实例——实例字段不得跨调用携带状态。
///
/// 实现类必须带 [ExposeServices(typeof(IScheduledJobHandler))]：ABP 约定注册只暴露
/// 「类名以接口名（去 I 前缀）结尾」的接口（05-reference-facts 13.5），handler 的命名
/// （如 AuditLogCleanupJobHandler）不满足这条后缀规则，不写特性就不会被注册成
/// IScheduledJobHandler，GetServices 拿到空集合——作业记录"未找到 handler"失败。
/// </summary>
public interface IScheduledJobHandler
{
    /// <summary>作业类型标识，全局唯一。建议用 "AbpAdmin.AuditLogCleanup" 这样的带前缀形式。</summary>
    string JobType { get; }

    /// <summary>展示名的本地化 key，前端下拉里显示。</summary>
    string DisplayNameKey { get; }

    Task ExecuteAsync(ScheduledJobContext context);
}

public class ScheduledJobContext
{
    public Guid ScheduledJobId { get; init; }

    public Guid? TenantId { get; init; }

    public string JobType { get; init; } = default!;

    /// <summary>作业配置里的 JSON 参数，为 null 时 handler 用默认值。</summary>
    public string? Payload { get; init; }

    public CancellationToken CancellationToken { get; init; }
}
