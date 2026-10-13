using System;
using System.Threading;
using System.Threading.Tasks;

namespace AbpAdmin.Imaging;

/// <summary>
/// 图片处理并发闸门（T3.1）。CPU 密集操作（解码/缩放/重编码）必须经它进入，
/// 全进程并发上限 MaxDegreeOfParallelism，等待闸门与执行各自受 ProcessTimeout 约束。
/// </summary>
public interface IImageProcessingThrottle
{
    /// <summary>
    /// 在并发闸门内执行 action。等待闸门超过 ProcessTimeout 抛
    /// <see cref="AbpAdminImageProcessingBusyException"/>（HTTP 503 + Retry-After）。
    /// action 拿到的 CancellationToken 是调用方 token 与 ProcessTimeout 的链接 token。
    /// </summary>
    Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default);
}
