using System;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// RetryAfter 解析器。
/// 实现方案 3.2/Q26 等待时间优先级规则：合法 HTTP header 优先，批准 body 字段兜底。
/// </summary>
public static class RetryAfterResolver
{
    /// <summary>
    /// 解析等待秒数。
    /// 优先级：合法 HTTP Retry-After header > 批准 body 字段 > null。
    /// 冲突时不拼值、不相加，取优先级高的有效值。
    /// </summary>
    /// <param name="headerValue">HTTP Retry-After header 值（秒数）。</param>
    /// <param name="bodyValue">响应 body 中的 retryAfterSeconds 字段值。</param>
    /// <returns>等待秒数，或 null 表示无有效等待时间。</returns>
    public static int? Resolve(int? headerValue, int? bodyValue)
    {
        // Header 优先：非负整数视为有效
        if (headerValue.HasValue && headerValue.Value >= 0)
        {
            return headerValue.Value;
        }

        // Body 兜底：非负整数视为有效
        if (bodyValue.HasValue && bodyValue.Value >= 0)
        {
            return bodyValue.Value;
        }

        return null;
    }

    /// <summary>
    /// 验证 RetryAfter 值是否合法。
    /// 合法值：非负整数。
    /// 超过 86400 秒（24 小时）不展示具体倒计时，但值本身不丢弃。
    /// </summary>
    public static bool IsValid(int? value)
    {
        return value.HasValue && value.Value >= 0;
    }

    /// <summary>
    /// 判断是否应展示具体倒计时。
    /// 超过 86400 秒不展示倒计时，改为通用提示。
    /// </summary>
    public static bool ShouldShowCountdown(int? value)
    {
        return value.HasValue && value.Value >= 0 && value.Value <= 86400;
    }
}
