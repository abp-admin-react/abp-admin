using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DistributedLocking;

namespace AbpAdmin.Collection.Payments;

internal readonly record struct CollectionMerchantRoute(CollectionChannel Channel, string MerchantAccountId)
{
    internal const string PaymentTask = "Collection:PaymentReconciliation";
    internal const string RefundTask = "Collection:RefundReconciliation";
    internal const int MaxConcurrentMerchants = 4;

    internal async Task RunAsync(string taskName, IServiceScopeFactory scopes, SemaphoreSlim slots,
        Func<IServiceProvider, CollectionMerchantRoute, CancellationToken, Task> reconcile, CancellationToken ct)
    {
        await slots.WaitAsync(ct);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var distributedLock = services.GetRequiredService<IAbpDistributedLock>();
            await using var lease = await distributedLock.TryAcquireAsync(
                $"{taskName}:{(int)Channel}:{MerchantAccountId}", cancellationToken: ct);
            if (lease != null) await reconcile(services, this, ct);
        }
        finally
        {
            slots.Release();
        }
    }
}
