using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Imaging;

/// <summary>
/// 基于 SemaphoreSlim 的进程级并发闸门。注册为单例（信号量本身即进程级状态）。
/// </summary>
public class ImageProcessingThrottle : IImageProcessingThrottle, ISingletonDependency, IDisposable
{
    private readonly SemaphoreSlim _semaphore;
    private readonly AbpAdminImagingOptions _options;

    public ImageProcessingThrottle(IOptions<AbpAdminImagingOptions> options)
    {
        _options = options.Value;
        _semaphore = new SemaphoreSlim(_options.MaxDegreeOfParallelism, _options.MaxDegreeOfParallelism);
    }

    public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken = default)
    {
        // 等闸门本身也要有上限，否则请求会无限堆积
        if (!await _semaphore.WaitAsync(_options.ProcessTimeout, cancellationToken))
        {
            throw new AbpAdminImageProcessingBusyException((int)Math.Ceiling(_options.ProcessTimeout.TotalSeconds));
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ProcessTimeout);
            return await action(cts.Token);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}
