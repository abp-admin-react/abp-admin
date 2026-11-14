using System;
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
        RunLoopAsync("Collection:PaymentEvents", (services, token) =>
            services.GetRequiredService<CollectionPaymentEventDispatcher>().DispatchPendingAsync(token), stoppingToken),
        RunLoopAsync("Collection:PaymentReconciliation", QueryPendingPaymentsAsync, stoppingToken),
        RunLoopAsync("Collection:RefundReconciliation", (services, token) =>
            services.GetRequiredService<ICollectionRefundService>().ReconcileAsync(token), stoppingToken));

    private async Task RunLoopAsync(
        string lockKey, Func<IServiceProvider, CancellationToken, Task> run, CancellationToken stoppingToken)
    {
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
                    await run(services, stoppingToken);
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

    private async Task QueryPendingPaymentsAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var gateways = services.GetServices<ICollectionChannelGateway>().ToArray();
        if (gateways.Length == 0)
        {
            return;
        }

        var clock = services.GetRequiredService<IClock>();
        var uowManager = services.GetRequiredService<IUnitOfWorkManager>();
        var dbProvider = services.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
        var paymentService = services.GetRequiredService<CollectionIntegrationService>();
        var now = clock.Now;
        foreach (var gateway in gateways)
        {
            Guid[] due;
            using (var uow = uowManager.Begin(requiresNew: true, isTransactional: false))
            {
                var db = await dbProvider.GetDbContextAsync();
                due = await db.Collections.AsNoTracking()
                    .Where(row => row.Status == CollectionStatus.Pending &&
                        row.Channel == gateway.Channel && row.MerchantAccountId == gateway.MerchantAccountId &&
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
                    logger.LogWarning(exception, "Collection {CollectionId} channel query failed and will be retried", id);
                }
            }
        }
    }
}
