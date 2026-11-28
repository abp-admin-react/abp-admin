using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Collection.Withdrawals;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

public class WithdrawGasBumpTests : CollectionTestBase
{
    private const string HotWallet = "0x19e7e376e7c213b7e7e7e46cc70a5dd086daff2a";
    private const string HotWalletKey = "0x1111111111111111111111111111111111111111111111111111111111111111";
    private const string UsdtContract = "0x55d398326f99059fF775485246999027B3197955";
    private static readonly Guid User1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1");

    private WithdrawManager Manager => GetRequiredService<WithdrawManager>();
    private CollectionBalanceManager Balances => GetRequiredService<CollectionBalanceManager>();
    private CollectionLedgerPoster Poster => GetRequiredService<CollectionLedgerPoster>();
    private WithdrawGuard Guard => GetRequiredService<WithdrawGuard>();
    private WithdrawBroadcastManager Broadcast => GetRequiredService<WithdrawBroadcastManager>();

    private async Task<FakeChainGateway> ArrangeWalletAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var network = await dbContext.Networks.OrderBy(n => n.CreationTime).FirstAsync();
        network.WithdrawVaultAddress = HotWallet;
        network.WithdrawVaultKeyCipher = await Guard.ProtectKeyAsync(HotWalletKey);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();

        var gateway = new FakeChainGateway(1000) { GasPriceGwei = 1m };
        gateway.TokenBalances[UsdtContract + ":" + HotWallet] = "10000000000000000000000";
        gateway.BnbBalances[HotWallet] = 1m;
        return gateway;
    }

    private async Task<ColNetwork> GetNetworkAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var net = await dbContext.Networks.AsNoTracking().OrderBy(n => n.CreationTime).FirstAsync();
        await uow.CompleteAsync();
        return net;
    }

    [Fact]
    public async Task Withdraw_Broadcast_Applies_RetryCount_Gas_Bump_And_Persists_Attempt()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        await Balances.CreditAsync(User1, CollectionCurrency.Usdt, 500m, $"dep-{Guid.NewGuid()}");
        await Poster.PostOpeningAsync(User1, CollectionCurrency.Usdt, 500m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt, "0x3333333333333333333333333333333333333333", 50m);
        await Manager.ApproveAsync(order.Id, null, "ok");

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.WithdrawOrders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.RetryCount, 2));
            await uow.CompleteAsync();
        }

        await Broadcast.BroadcastQueueAsync(network, gateway, 10);

        gateway.BroadcastCount.ShouldBe(1);
        var sentHash = gateway.Broadcasts.Single().Hash;
        var expectedGasWei = BscGasPricePolicy.ApplyBump(BscGasPricePolicy.GweiToWei(1m), 2);
        gateway.BroadcastGasPrices[sentHash].ShouldBe(expectedGasWei);

        using var check = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var checkDb = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var attempt = await checkDb.ChainTxAttempts.AsNoTracking()
            .SingleAsync(a => a.Purpose == ChainTxPurpose.WithdrawPayout && a.RefId == order.Id);
        attempt.TxHash.ShouldBe(sentHash);
        attempt.AttemptNo.ShouldBe(3);
        attempt.GasPriceWei.ShouldBe(expectedGasWei.ToString());
        attempt.Status.ShouldBe(ChainTxAttemptStatus.Broadcasted);
    }

    [Fact]
    public async Task Withdraw_Broadcast_Refuses_Silent_Gas_Cap_When_HotWallet_Bnb_Insufficient_For_Bump()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        await Balances.CreditAsync(User1, CollectionCurrency.Usdt, 500m, $"dep-{Guid.NewGuid()}");
        await Poster.PostOpeningAsync(User1, CollectionCurrency.Usdt, 500m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt, "0x3333333333333333333333333333333333333333", 50m);
        await Manager.ApproveAsync(order.Id, null, "ok");

        // MinHotWalletBnb 默认 0.005m; 若设 RetryCount=4 且 GasPrice=50 Gwei -> 50 * 1.25^4 = 122 Gwei -> 100k*122 Gwei = 0.0122 BNB > 0.006 BNB
        gateway.GasPriceGwei = 50m;
        gateway.BnbBalances[HotWallet] = 0.006m;
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.WithdrawOrders.Where(o => o.Id == order.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.RetryCount, 4));
            await uow.CompleteAsync();
        }

        await Broadcast.BroadcastQueueAsync(network, gateway, 10);

        gateway.BroadcastCount.ShouldBe(0, "BNB 不足抬价后期望 gas 时禁止静默压价广播");
        using var check = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var checkDb = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        (await checkDb.WithdrawOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id)).Status.ShouldBe(WithdrawStatus.Processing);
    }

    [Fact]
    public async Task NoHash_Timeout_Recovers_PrePersisted_Attempt_Without_Explorer()
    {
        var gateway = await ArrangeWalletAsync();
        var network = await GetNetworkAsync();
        await Balances.CreditAsync(User1, CollectionCurrency.Usdt, 500m, $"dep-{Guid.NewGuid()}");
        await Poster.PostOpeningAsync(User1, CollectionCurrency.Usdt, 500m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt, "0x3333333333333333333333333333333333333333", 50m);
        await Manager.ApproveAsync(order.Id, null, "ok");

        // 模拟发送后节点已接收但保存 Order.TxHash 失败：ColChainTxAttempts 已在广播前落库
        await Broadcast.BroadcastQueueAsync(network, gateway, 10);
        var broadcastHash = gateway.Broadcasts.Single().Hash;

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.WithdrawOrders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.TxHash, (string?)null)
                .SetProperty(o => o.StatusChangedAt, DateTime.UtcNow.AddHours(-2)));
            await uow.CompleteAsync();
        }

        // 回执可见但 GetTransfersForAddress 未配置任何 Etherscan 数据 → 凭 ColChainTxAttempts 认领并下轮完结
        gateway.HasReceipt = true;
        gateway.ReceiptHashes = [broadcastHash];

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        using (var checkUow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var adopted = await (await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync())
                .WithdrawOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
            await checkUow.CompleteAsync();
            adopted.TxHash.ShouldBe(broadcastHash);
        }

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        using var finalUow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var finalOrder = await (await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync())
            .WithdrawOrders.AsNoTracking().SingleAsync(o => o.Id == order.Id);
        await finalUow.CompleteAsync();
        finalOrder.Status.ShouldBe(WithdrawStatus.Completed);
    }
}
