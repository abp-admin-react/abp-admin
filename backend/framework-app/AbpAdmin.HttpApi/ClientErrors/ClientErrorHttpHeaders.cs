using Microsoft.AspNetCore.Http;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 错误响应头：Retry-After 封顶与写出顺序。
/// <c>Response.Clear()</c> 之后必须重写，否则等待秒数丢失。
/// </summary>
internal static class ClientErrorHttpHeaders
{
    internal const int MaxRetryAfterSeconds = 86400;

    /// <summary>
    /// 写出 Retry-After（delta-seconds）。负值忽略；超过 86400 秒封顶，拒绝无界倒计时。
    /// </summary>
    /// <param name="response">尚未开始写体的响应。</param>
    /// <param name="retryAfterSeconds">投影批准的等待秒数；null 或负数不写头。</param>
    internal static void ApplyRetryAfter(HttpResponse response, int? retryAfterSeconds)
    {
        if (retryAfterSeconds is not int seconds || seconds < 0)
        {
            return;
        }

        if (seconds > MaxRetryAfterSeconds)
        {
            seconds = MaxRetryAfterSeconds;
        }

        response.Headers.RetryAfter = seconds.ToString();
    }
}
