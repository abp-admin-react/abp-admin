using Volo.Abp;
using Volo.Abp.ExceptionHandling;

namespace AbpAdmin.Imaging;

/// <summary>
/// 图片处理并发闸门等待超时异常。HTTP 状态码 503。
/// Retry-After 响应头由 HttpApi 层的 ImageProcessingBusyExceptionFilter 根据
/// <see cref="RetryAfterSeconds"/> 设置（与 T2.5 的 429 异常同款 IHasHttpStatusCode 模式）。
/// </summary>
public class AbpAdminImageProcessingBusyException : BusinessException, IHasHttpStatusCode
{
    public int HttpStatusCode => 503;

    /// <summary>建议客户端多少秒后重试（取 ProcessTimeout 的秒数，向上取整）。</summary>
    public int RetryAfterSeconds { get; }

    public AbpAdminImageProcessingBusyException(int retryAfterSeconds)
        : base(AbpAdminDomainErrorCodes.Imaging.ImageProcessingBusy)
    {
        RetryAfterSeconds = retryAfterSeconds;
        this.WithData(nameof(RetryAfterSeconds), retryAfterSeconds);
    }
}
