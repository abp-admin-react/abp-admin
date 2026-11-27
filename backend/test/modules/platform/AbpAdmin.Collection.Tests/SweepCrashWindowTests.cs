using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 旧归集任务中断后可能仍关联超出广播金额的进款单。验证恢复时的选单上限和入账前的毛额核对。
/// </summary>
public class SweepCrashWindowTests : CollectionTestBase
{
    private static readonly Guid UserA = Guid.Parse("c1111111-1111-1111-1111-111111111111");
    private static readonly Guid UserB = Guid.Parse("c2222222-2222-2222-2222-222222222222");

    private SweepManager Manager => GetRequiredService<SweepManager>().WithMasterKey("test-master-key");

    private async Task<(ColNetwork Network, ColToken Token, Guid AddressId, string Watched)> SeedAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var encryptor = GetRequiredService<Volo.Abp.Security.Encryption.IStringEncryptionService>();
        var mnemonicCipher = HdKeyService.Protect(
            "test test test test test test test test test test test test", "test-master-key", encryptor);
        var network = new ColNetwork(Guid.NewGuid())
        {
            Name = "BSC-CW-" + Guid.NewGuid().ToString("N")[..8],
            ChainId = 97,
            NodeLogResultCap = 100,
            AutoCreditMaxAmount = 5000m,
            RpcCipher = "x",
            MnemonicCipher = mnemonicCipher,
            CreationTime = DateTime.Now,
        };
        await dbContext.Networks.AddAsync(network);
        var token = new ColToken(Guid.NewGuid())
        {
            NetworkId = network.Id, Symbol = "USDT", ContractAddress = "0xusdt-cw",
            Decimals = 18, MinDeposit = "10", IsEnabled = true,
            LastScannedBlock = 100,
            VaultAddress = "0xvault-cw",
            CreationTime = DateTime.Now,
        };
        await dbContext.Tokens.AddAsync(token);
        var watched = "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];
        var addressId = Guid.NewGuid();
        await dbContext.DepositAddresses.AddAsync(new ColDepositAddress(addressId)
        {
            UserId = UserA, NetworkId = network.Id, Address = watched,
            AddressIndex = 7, CreationTime = DateTime.Now,
        });
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return (network, token, addressId, watched);
    }

    /// <summary>关联两笔 Confirmed 进款，任务金额只包含第一笔。</summary>
    private async Task<Guid> SeedPoisonedTaskAsync(
        CollectionDbContext dbContext, ColNetwork network, ColToken token, Guid addressId, string watched,
        SweepTaskStatus status, string? txHash)
    {
        var taskId = Guid.NewGuid();
        var orderA = new ColDepositOrder(Guid.NewGuid())
        {
            NetworkId = network.Id, TokenId = token.Id, UserId = UserA, AddressId = addressId,
            ToAddress = watched, FromAddress = "0xfrom",
            TxHash = "0xdep-a-" + Guid.NewGuid().ToString("N"), LogIndex = 1,
            RawAmount = "0", Amount = 10m, BlockNumber = 120,
            Status = DepositOrderStatus.Confirmed, SweepTaskId = taskId,
            CreationTime = DateTime.Now.AddMinutes(-2),
        };
        var orderB = new ColDepositOrder(Guid.NewGuid())
        {
            NetworkId = network.Id, TokenId = token.Id, UserId = UserB, AddressId = addressId,
            ToAddress = watched, FromAddress = "0xfrom",
            TxHash = "0xdep-b-" + Guid.NewGuid().ToString("N"), LogIndex = 2,
            RawAmount = "0", Amount = 5m, BlockNumber = 120,
            Status = DepositOrderStatus.Confirmed, SweepTaskId = taskId,
            CreationTime = DateTime.Now.AddMinutes(-1),
        };
        await dbContext.DepositOrders.AddRangeAsync(orderA, orderB);
        await dbContext.SweepTasks.AddAsync(new ColSweepTask(taskId)
        {
            NetworkId = network.Id, TokenId = token.Id, UserId = UserA, AddressId = addressId,
            Address = watched, AddressIndex = 7, Amount = 10m,
            Status = status, TxHash = txHash, BroadcastAt = txHash != null ? DateTime.Now.AddMinutes(-30) : null,
            StatusChangedAt = DateTime.Now, CreationTime = DateTime.Now,
        });
        await dbContext.SaveChangesAsync();
        return taskId;
    }

    private async Task RunRoundAsync(ColNetwork network, ColToken token, FakeChainGateway gateway)
    {
        await Manager.RunTokenRoundAsync(network, token, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
    }

    [Fact]
    public async Task Legacy_Partial_Activation_SelfHeal_Does_Not_Promote_Over_Broadcast_Amount()
    {
        // 任务金额 10 只覆盖第一笔，金额 5 的第二笔应保持 Confirmed。
        var (network, token, addressId, watched) = await SeedAsync();
        var gateway = new FakeChainGateway(head: 500) { HasReceipt = true, ReceiptConfirmationsMet = true };

        Guid taskId;
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            taskId = await SeedPoisonedTaskAsync(dbContext, network, token, addressId, watched,
                SweepTaskStatus.Sweeping, txHash: null);
            await uow.CompleteAsync();
        }

        await RunRoundAsync(network, token, gateway);

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var orders = await dbContext.DepositOrders.AsNoTracking()
                .Where(o => o.SweepTaskId == taskId)
                .ToDictionaryAsync(o => o.Amount ?? 0m, o => o.Status);
            orders[10m].ShouldBe(DepositOrderStatus.Sweeping, "A（广播子集内）自愈升级");
            orders[5m].ShouldBe(DepositOrderStatus.Confirmed, "B（尾巴）不得越过广播金额被升级");
            var pushed = Fakes.FakeChainPushNotifier.Instance.Deposits.ShouldHaveSingleItem();
            pushed.Amount.ShouldBe("10");
            pushed.Status.ShouldBe(nameof(DepositOrderStatus.Sweeping));
        }
    }

    [Fact]
    public async Task Credit_Time_Amount_Mismatch_Refuses_Credit_And_Fails_Task_For_Manual()
    {
        // 链上只转了 10，待结算的进款却合计 15；两笔都应留待人工核对。
        var (network, token, addressId, watched) = await SeedAsync();
        var gateway = new FakeChainGateway(head: 500) { HasReceipt = true, ReceiptConfirmationsMet = true };
        gateway.Logs[token.ContractAddress] = new()
        {
            new TokenTransferLog("0xpoisoned", 0, watched, token.VaultAddress!, "10000000000000000000", 400),
        };

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var taskId = await SeedPoisonedTaskAsync(dbContext, network, token, addressId, watched,
                SweepTaskStatus.Sweeping, txHash: null);
            // 模拟旧任务已关联两笔 Sweeping 单并持有广播哈希。
            await dbContext.DepositOrders
                .Where(o => o.SweepTaskId == taskId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, DepositOrderStatus.Sweeping));
            await dbContext.SweepTasks
                .Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.TxHash, "0xpoisoned")
                    .SetProperty(t => t.BroadcastAt, DateTime.Now.AddMinutes(-30)));
            await uow.CompleteAsync();
        }

        await RunRoundAsync(network, token, gateway);

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var task = await dbContext.SweepTasks.AsNoTracking().SingleAsync(t => t.TokenId == token.Id);
            task.Status.ShouldBe(SweepTaskStatus.Failed);
            task.Retryable.ShouldBeFalse("金额漂移必须人工核对，不得自动重试");
            task.FailReason!.ShouldContain("链接单毛额合计");

            var orders = await dbContext.DepositOrders.AsNoTracking()
                .Where(o => o.SweepTaskId == task.Id)
                .ToListAsync();
            orders.ShouldAllBe(o => o.Status == DepositOrderStatus.Sweeping, "单保持 Sweeping 不入账");

            var credited = await dbContext.Balances.AsNoTracking()
                .Where(b => b.UserId == UserA || b.UserId == UserB)
                .ToListAsync();
            credited.ShouldBeEmpty("毛额合计 ≠ 广播金额时一分钱都不能入账");
        }
    }
}
