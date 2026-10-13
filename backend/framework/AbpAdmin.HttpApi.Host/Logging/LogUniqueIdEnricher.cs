using System;
using Serilog.Core;
using Serilog.Events;

namespace AbpAdmin.Logging;

/// <summary>
/// 每条日志事件附加一个全局唯一短 ID（属性 <c>LogUniqueId</c>）。
///
/// 定位:CorrelationId 解决"同一请求的多条日志互相关联"(X-Correlation-Id 贯穿请求/响应/日志),
/// 本 enricher 解决"从海量日志中单点跳到一条"——用户报障只需给一个 ID,运维即可精确定位,
/// 尤其覆盖无请求上下文的场景(后台作业、启动期、Quartz 触发)。
///
/// 取值:GUID 的 N 格式前 12 位 hex(48 bit)。日志查询是"给 ID 找唯一行",不需要密码学强度,
/// 12 位足以在任意实际日志体量内无碰撞感知,且比完整 GUID 在控制台/文件里更省横向空间。
/// 借鉴 abp-next-admin 的 UniqueIdEnricher 思路,但用零依赖的 GUID 代替其雪花 ID 生成器
/// (不引入 IDistributedIdGenerator 的 WorkerId 配置负担)。
/// </summary>
public class LogUniqueIdEnricher : ILogEventEnricher
{
    public const string PropertyName = "LogUniqueId";

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
            PropertyName, Guid.NewGuid().ToString("N")[..12]));
    }
}
