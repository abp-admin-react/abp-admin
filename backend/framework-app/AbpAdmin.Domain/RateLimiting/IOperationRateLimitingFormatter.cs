using System;

namespace AbpAdmin.RateLimiting;

/// <summary>
/// 限流文案格式化器。定制异常消息与 WindowDescription 的文案格式。
/// 默认实现产出中文描述，如「每 1 小时最多 3 次」。
/// </summary>
public interface IOperationRateLimitingFormatter
{
    /// <summary>
    /// 生成窗口的人类可读描述，如「每 1 小时最多 3 次」。
    /// </summary>
    string FormatWindowDescription(TimeSpan duration, int maxCount);
}
