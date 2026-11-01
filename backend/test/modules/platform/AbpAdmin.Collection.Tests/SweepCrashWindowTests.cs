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
/// 回归锁（sweep 激活崩溃窗）：激活的 claim/翻状态/解链若为三条独立自动提交，
/// 中断窗口留下「任务 Amount 已按选中子集聚合、尾巴单仍链接+Confirmed」的毒态；自愈无条件
/// 升级 + 确认逐笔入账 ⇒ 把没上链的钱记给用户（金库缺口）。修复三道：激活内层事务原子化
/// （新毒态不可达）、自愈升级按 task.Amount 截额（存量毒态不再扩大）、确认入账前毛额对账门
/// （漂移即拒绝入账转人工）。本文件针对存量毒态验证第二、三道的 fail-closed 行为。
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

    /// <summary>种毒态：任务 + 两笔链接 Confirmed 单（A 广播子集内、B 尾巴），Amount 只含 A。</summary>
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
        // 第二道：自愈升级按 task.Amount 截额。毒态（A=10 Confirmed 链接、B=5 尾巴 Confirmed
        // 链接、Amount=10）下自愈只升 A；B 留在 Confirmed，不会被并入链上只转 10 的归集。
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
        }
    }

    [Fact]
    public async Task Credit_Time_Amount_Mismatch_Refuses_Credit_And_Fails_Task_For_Manual()
    {
        // 第三道（资金时刻对账门）：历史毒态里 B 已被（修复前的）自愈升级成 Sweeping——
        // 链上只转 10、链接单毛额合计 15。修复前：逐笔入账 15 = 金库缺口 5。
        // 修复后：拒绝入账、任务置失败仅人工、单保持 Sweeping。
        var (network, token, addressId, watched) = await SeedAsync();
        var gateway = new FakeChainGateway(head: 500) { HasReceipt = true, ReceiptConfirmationsMet = true };

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var taskId = await SeedPoisonedTaskAsync(dbContext, network, token, addressId, watched,
                SweepTaskStatus.Sweeping, txHash: null);
            // 模拟修复前自愈留下的毒态：A、B 都已是 Sweeping，链上"已广播"（TxHash 占位）
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
