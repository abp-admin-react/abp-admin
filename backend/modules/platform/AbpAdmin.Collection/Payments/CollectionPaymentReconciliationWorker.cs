using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Channels;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Payments;

public sealed class CollectionPaymentReconciliationWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<CollectionPaymentReconciliationWorker> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(
        RunPaymentEventLoopAsync(stoppingToken),
        RunMerchantLoopsAsync(TimeSpan.FromSeconds(10), stoppingToken));

    internal Task RunMerchantLoopsAsync(TimeSpan interval, CancellationToken stoppingToken) => Task.WhenAll(
        RunMerchantLoopAsync(CollectionMerchantRoute.PaymentTask, services => services.GetServices<ICollectionChannelGateway>()
                .Select(gateway => new CollectionMerchantRoute(gateway.Channel, gateway.MerchantAccountId)),
            QueryPendingPaymentsAsync, interval, stoppingToken),
        RunMerchantLoopAsync(CollectionMerchantRoute.RefundTask, services => CollectionRefundService.Routes(
                services.GetServices<ICollectionRefundChannelGateway>()), CollectionRefundService.ReconcileRouteAsync,
            interval, stoppingToken));

    private async Task RunMerchantLoopAsync(string taskName, Func<IServiceProvider, IEnumerable<CollectionMerchantRoute>> routes,
        Func<IServiceProvider, CollectionMerchantRoute, CancellationToken, Task> reconcile, TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        using var slots = new SemaphoreSlim(CollectionMerchantRoute.MaxConcurrentMerchants);
        var running = new Dictionary<CollectionMerchantRoute, Task>();
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                foreach (var entry in running.Where(entry => entry.Value.IsCompleted).ToArray()) running.Remove(entry.Key);
                try
                {
                    await using var discovery = scopeFactory.CreateAsyncScope();
                    foreach (var route in routes(discovery.ServiceProvider).Distinct())
                    {
                        // 同一商户的上一轮仍在运行时跳过该商户，其余商户继续排队。
                        if (!running.ContainsKey(route)) running.Add(route, RunAsync(route));
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    logger.LogError(ex, "Could not discover merchant routes for {TaskName}", taskName);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            await Task.WhenAll(running.Values);
        }

        async Task RunAsync(CollectionMerchantRoute route)
        {
            try
            {
                await route.RunAsync(taskName, scopeFactory, slots, reconcile, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex)
            {
                logger.LogError(ex, "{TaskName} failed for {Channel}/{MerchantAccountId}", taskName, route.Channel, route.MerchantAccountId);
            }
        }
    }

    private async Task RunPaymentEventLoopAsync(CancellationToken stoppingToken)
    {
        const string lockKey = "Collection:PaymentEvents";
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var services = scope.ServiceProvider;
                var distributedLock = services.GetRequiredService<IAbpDistributedLock>();
                await using var round = await distributedLock.TryAcquireAsync(lockKey, cancellationToken: stoppingToken);
                if (round != null)
                {
                    await services.GetRequiredService<CollectionPaymentEventDispatcher>().DispatchPendingAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Collection background task {TaskName} failed and will retry", lockKey);
            }
        }
    }

    private async Task QueryPendingPaymentsAsync(IServiceProvider services, CollectionMerchantRoute route, CancellationToken cancellationToken)
    {
        var clock = services.GetRequiredService<IClock>();
        var uowManager = services.GetRequiredService<IUnitOfWorkManager>();
        var dbProvider = services.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
        var paymentService = services.GetRequiredService<CollectionIntegrationService>();
        var now = clock.Now;
        Guid[] due;
        using (var uow = uowManager.Begin(requiresNew: true, isTransactional: false))
        {
            var db = await dbProvider.GetDbContextAsync();
            due = await db.Collections.AsNoTracking()
                .Where(row => row.Status == CollectionStatus.Pending &&
                    row.Channel == route.Channel && row.MerchantAccountId == route.MerchantAccountId &&
                    (row.ChannelCreateLeaseUntil == null || row.ChannelCreateLeaseUntil <= now) &&
                    (row.NextChannelQueryAt == null || row.NextChannelQueryAt <= now))
                .OrderBy(row => row.NextChannelQueryAt)
                .ThenBy(row => row.CreationTime)
                .ThenBy(row => row.Id)
                .Take(10)
                .Select(row => row.Id)
                .ToArrayAsync(cancellationToken);
            await uow.CompleteAsync(cancellationToken);
        }

        foreach (var id in due)
        {
            try
            {
                using (var uow = uowManager.Begin(requiresNew: true, isTransactional: true))
                {
                    var db = await dbProvider.GetDbContextAsync();
                    var deferUntil = clock.Now.AddSeconds(30);
                    // 先提交下次查询时间，避免失败记录在后续批次中持续占据队首。
                    var updated = await db.Collections.Where(row => row.Id == id && row.Status == CollectionStatus.Pending &&
                        (row.ChannelCreateLeaseUntil == null || row.ChannelCreateLeaseUntil <= now) &&
                        (row.NextChannelQueryAt == null || row.NextChannelQueryAt <= now))
                        .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.NextChannelQueryAt, deferUntil),
                            cancellationToken);
                    await uow.CompleteAsync(cancellationToken);
                    if (updated == 0)
                    {
                        continue;
                    }
                }

                await paymentService.QueryChannelAsync(id, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Pending payment reconciliation failed for {CollectionId} on {Channel}/{MerchantAccountId}",
                    id, route.Channel, route.MerchantAccountId);
            }
        }
    }
}
