using Volo.Abp;
using Volo.Abp.ExceptionHandling;

namespace AbpAdmin.Imaging;

/// <summary>
/// 图片处理并发闸门等待超时异常。HTTP 状态码 503。
/// Retry-After 由 <c>ClientErrorHttpProjector</c> 从 <see cref="RetryAfterSeconds"/> 投影，
/// 经 Filter / Writer 在 Clear 之后写出（与限流 429 同款 IHasHttpStatusCode 模式）。
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
