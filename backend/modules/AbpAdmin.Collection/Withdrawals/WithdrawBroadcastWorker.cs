using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Withdrawals;

/// <summary>
/// 提现广播 worker（M3 薄壳）：轮次逻辑在 <see cref="WithdrawBroadcastManager"/>（可注入假网关直测）。
/// 开关 Collection:Withdraw:Enabled（默认 false——纯睡眠不取锁不碰链）；Redis 轮锁 + 墙钟预算
/// 与扫链/归集 worker 同纪律（CollectionChainWorkerBase）。
/// </summary>
public class WithdrawBroadcastWorker : CollectionChainWorkerBase
{
    private readonly CollectionChainWorkerOptions _options;

    public WithdrawBroadcastWorker(
        IAbpDistributedLock distributedLock,
        IServiceScopeFactory scopeFactory,
        CollectionChainWorkerOptions options,
        ILogger<WithdrawBroadcastWorker> logger)
        : base(distributedLock, scopeFactory,
            TimeSpan.FromSeconds(options.WithdrawBroadcastIntervalSeconds),
            TimeSpan.FromSeconds(options.WithdrawRoundBudgetSeconds),
            logger)
    {
        _options = options;
    }

    protected override string LockKey => CollectionConsts.WithdrawBroadcastLockKey;

    /// <summary>测试桥：宿主注册测试断言轮锁键（internal + InternalsVisibleTo）。</summary>
    internal string LockKeyExposeForTest() => LockKey;

    protected override bool Enabled => _options.WithdrawEnabled;

    protected override async Task RunRoundAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        var dbContextProvider = scopedServices.GetRequiredService<IDbContextProvider<CollectionDbContext>>();
        var factory = scopedServices.GetRequiredService<ChainGatewayFactory>();
        var manager = scopedServices.GetRequiredService<WithdrawBroadcastManager>();
        var uowManager = scopedServices.GetRequiredService<IUnitOfWorkManager>();

        using var uow = uowManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await dbContextProvider.GetDbContextAsync();
        var network = await dbContext.Networks.AsNoTracking()
            .OrderBy(n => n.CreationTime).FirstOrDefaultAsync(ct);
        if (network == null)
        {
            return;
        }

        // 广播只需要 eth_* 调用（nonce/余额/签名/回执），不扫日志——但沿用扫链的网关组装纪律
        //（key 池优先、RpcCipher 回退、cap 未测量拒绝工作），运维只需维护一套 RPC 配置
        var gateway = await CreateGatewayAsync(factory, dbContext, network, Logger);
        await uow.CompleteAsync();
        if (gateway == null)
        {
            return;
        }

        await manager.RunRoundAsync(network, gateway,
            _options.WithdrawAutoApproveBatchSize, _options.WithdrawBroadcastBatchSize, ct);
    }
}
