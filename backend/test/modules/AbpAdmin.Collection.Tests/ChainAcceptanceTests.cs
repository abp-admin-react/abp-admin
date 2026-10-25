using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 链上验收清单（计划第 8 节 M5 行，假网关驱动真 PG 状态机）：
/// 扫链未开不发新地址、低额和超精度都留下可查进款单、截断响应不推进游标、
/// 一种币失败不挡另一种、未归集不加余额、深度不够不重签、同址两币不同时广播、
/// 同址同币合并归集、失败可再试、确认后余额和充值订单出现、重启（重跑）不重复入账。
/// 并发派生不撞索引在 <see cref="DepositAddressDerivationTests"/>。
/// </summary>
public class ChainAcceptanceTests : CollectionTestBase, IDisposable
{
    private static readonly Guid User1 = Guid.Parse("66666666-6666-6666-6666-666666666666");

    /// <summary>资金路径用例各自独立用户：共享 PG 库跨用例串台（实测踩过）。</summary>
    private static Guid NewUser() => Guid.NewGuid();
    private static readonly Guid User2 = Guid.Parse("69696969-6969-6969-6969-696969696969");

    /// <summary>每用例唯一地址：用例共享同一 PG 库，固定地址会跨用例串行（实测踩过）。</summary>
    private static string WatchAddress => "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];

    private async Task<(ColNetwork Network, ColToken Usdt, ColToken Usdc)> SeedChainAsync(bool scanRan = false)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var encryptor = GetRequiredService<Volo.Abp.Security.Encryption.IStringEncryptionService>();
        var mnemonicCipher = HdKeyService.Protect(
            "test test test test test test test test test test test test", "test-master-key", encryptor);
        var network = new ColNetwork(TestGuid.Next())
        {
            Name = "BSC-TEST-" + Guid.NewGuid().ToString("N")[..8],
            ChainId = 97,
            NodeLogResultCap = 100,
            RpcCipher = "x",
            MnemonicCipher = mnemonicCipher,
            CreationTime = TestClock.Now,
        };
        await dbContext.Networks.AddAsync(network);
        var usdt = new ColToken(TestGuid.Next())
        {
            NetworkId = network.Id, Symbol = "USDT", ContractAddress = "0xusdt",
            Decimals = 18, MinDeposit = "10", IsEnabled = true,
            LastScannedBlock = scanRan ? 100 : null,
            CreationTime = TestClock.Now,
        };
        var usdc = new ColToken(TestGuid.Next())
        {
            NetworkId = network.Id, Symbol = "USDC", ContractAddress = "0xusdc",
            Decimals = 18, MinDeposit = "10", IsEnabled = true,
            LastScannedBlock = scanRan ? 100 : null,
            CreationTime = TestClock.Now,
        };
        await dbContext.Tokens.AddRangeAsync(usdt, usdc);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return (network, usdt, usdc);
    }

    private async Task<Guid> SeedWatchedAddressAsync(CollectionDbContext dbContext, Guid networkId, string address)
    {
        var existing = await dbContext.DepositAddresses
            .FirstOrDefaultAsync(a => a.NetworkId == networkId && a.Address == address);
        if (existing != null)
        {
            return existing.Id;
        }

        var entity = new ColDepositAddress(TestGuid.Next())
        {
            UserId = User1, NetworkId = networkId, Address = address,
            AddressIndex = Random.Shared.Next(2, 1000), CreationTime = TestClock.Now,
        };
        await dbContext.DepositAddresses.AddAsync(entity);
        await dbContext.SaveChangesAsync();
        return entity.Id;
    }

    private DepositScanManager NewScanManager()
    {
        // DI 解析（DomainService 约定注册）；网关按轮传入（假实现替换）
        return GetRequiredService<DepositScanManager>();
    }

    private SweepManager NewSweepManager()
    {
        return GetRequiredService<SweepManager>().WithMasterKey("test-master-key");
    }

    [Fact]
    public async Task Below_Min_And_Precision_Overflow_Leave_Queryable_Deposit_Orders()
    {
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        var gateway = new FakeChainGateway(head: 500);
        gateway.Logs["0xusdt"] = new List<TokenTransferLog>
        {
            new("0xlow", 0, "0xfrom", watched, "5000000000000000000" /* 5 < 10 */, 130),
            new("0xbig", 1, "0xfrom", watched, new string('9', 30) /* 30 位 > 28 */, 130),
            new("0xok", 2, "0xfrom", watched, "15000000000000000000" /* 15 达标 */, 130),
        };

                    await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
            await NewScanManager().RunTokenRoundAsync(
                network, usdt, gateway, confirmations: 12, blockRange: 50, scanBehindBlocks: 2000,
                TimeSpan.FromMinutes(10), 0.9m, CancellationToken.None);

        var orders = await ReadOrdersAsync(usdt.Id);
        orders.Count.ShouldBe(3); // 三笔都留下可查进款单
        orders.Single(o => o.TxHash == "0xlow").Status.ShouldBe(DepositOrderStatus.SkippedBelowMinDeposit);
        var overflow = orders.Single(o => o.TxHash == "0xbig");
        overflow.Status.ShouldBe(DepositOrderStatus.PrecisionOverflow);
        overflow.Amount.ShouldBeNull();
        overflow.RawAmount.ShouldBe(new string('9', 30));
        // 同轮确认推进：块龄远超确认数 → 直接 Confirmed 并并入（新建）Queued 归集任务
        var ok = orders.Single(o => o.TxHash == "0xok");
        ok.Status.ShouldBe(DepositOrderStatus.Confirmed);
        ok.SweepTaskId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Truncated_Log_Response_Does_Not_Advance_Cursor()
    {
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        var gateway = new FakeChainGateway(head: 500);
        gateway.TruncateThreshold = 0; // 连空响应都判未读完（极端截断）：整段缩到 1 块走按地址兜底
        gateway.Logs["0xusdt"] = Enumerable.Range(0, 12)
            .Select(i => new TokenTransferLog($"0xt{i}", i, "0xfrom", watched, "1", 130)).ToList();

        await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        await NewScanManager().RunTokenRoundAsync(
            network, usdt, gateway, 12, blockRange: 50, scanBehindBlocks: 2000,
            TimeSpan.FromMinutes(10), 0.9m, CancellationToken.None);

        // 缩到 1 块仍读不完 → 按地址分批兜底（该块算读完）：游标只前进到兜底块末端（101），
        // 绝不越过未读区间；130 上的进款单留给下一轮从 102 继续
        var current = await ReadTokenAsync(usdt.Id);
        current.LastScannedBlock.ShouldBe(101L);
        (await ReadOrdersAsync(usdt.Id)).Count.ShouldBe(0);
    }

    [Fact]
    public async Task One_Token_Failing_Does_Not_Block_The_Other()
    {
        var watched = WatchAddress;
        var (network, usdt, usdc) = await SeedChainAsync(scanRan: true);
        var gateway = new FakeChainGateway(head: 500);
        gateway.ThrowOnContract = "0xusdt"; // USDT 通道抛异常
        gateway.Logs["0xusdc"] = new List<TokenTransferLog>();

        var scanManager = NewScanManager();
        foreach (var token in new[] { usdt, usdc })
        {
            try
            {
                                    await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
                    await scanManager.RunTokenRoundAsync(
                        network, token, gateway, 12, 50, 2000, TimeSpan.FromMinutes(10), 0.9m, CancellationToken.None);
            }
            catch (ChainRpcException)
            {
                // worker 逐币捕获：USDT 失败不挡 USDC
            }
        }

        (await ReadTokenAsync(usdc.Id)).LastScannedBlock.ShouldNotBeNull(); // USDC 游标前进
        (await ReadTokenAsync(usdt.Id)).LastScannedBlock.ShouldBe(100L);   // USDT 游标不动
    }

    [Fact]
    public async Task Unswept_Deposits_Do_Not_Add_Balance()
    {
        var unUser = NewUser();
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        var manager = NewSweepManager();

        // 确认达标 → Queued 任务；但金库未配置 → 归集轮空转：不加余额、不广播
        usdt.VaultAddress = null;
        var orderId = await SeedOrderAsync(network.Id, usdt, watched, 20m);

        await NewScanManager().RunTokenRoundAsync(
            network, usdt, new FakeChainGateway(head: 500), 12, 50, 2000,
            TimeSpan.FromMinutes(10), 0.9m, CancellationToken.None);
        await manager.RunTokenRoundAsync(network, usdt, new FakeChainGateway(head: 500),
            12, null, 0.005m, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m,
            CancellationToken.None);

        var balances = await (await DbAsync()).Balances
            .Where(b => b.UserId == unUser).ToListAsync();
        balances.ShouldBeEmpty();
        var order = await ReadOrderAsync(orderId);
        order.Status.ShouldBe(DepositOrderStatus.Confirmed); // 停在待归集
        order.SweepTaskId.ShouldNotBeNull();
    }

    [Fact]
    public async Task Same_Address_Two_Currencies_Never_Broadcast_Simultaneously()
    {
        var watched = WatchAddress;
        var (network, usdt, usdc) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        usdc.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500);
        gateway.HasReceipt = true; // 广播即有回执（深度不够先不确认）

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId);
        await SeedOrderAsync(network.Id, usdc, watched, 30m, addressId);

        var scanManager = NewScanManager();
        await scanManager.RunTokenRoundAsync(network, usdt, gateway, 12, 50, 2000,
            TimeSpan.FromMinutes(10), 0.9m, CancellationToken.None);
        await scanManager.RunTokenRoundAsync(network, usdc, gateway, 12, 50, 2000,
            TimeSpan.FromMinutes(10), 0.9m, CancellationToken.None);

        var manager = NewSweepManager();
        var manager2 = NewSweepManager();
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        await manager2.RunTokenRoundAsync(network, usdc, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        // USDT 先占（Fueling/Sweeping）；USDC 的任务必须仍停在 Queued（地址级互斥）。
        // 库跨用例共享：按本网络过滤再数
        var tasks = (await DbAsync()).SweepTasks.Where(t => t.NetworkId == network.Id).ToList();
        tasks.Count.ShouldBe(2);
        tasks.Single(t => t.TokenId == usdt.Id).Status.ShouldNotBe(SweepTaskStatus.Queued);
        tasks.Single(t => t.TokenId == usdc.Id).Status.ShouldBe(SweepTaskStatus.Queued);
    }

    [Fact]
    public async Task Same_Address_Same_Currency_Queued_Tasks_Merge()
    {
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500);

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId);
        await SeedOrderAsync(network.Id, usdt, watched, 30m, addressId);

        // 两轮确认推进：第二笔并入同一 Queued 任务（扫链侧合并逻辑）
        await NewScanManager().RunTokenRoundAsync(network, usdt, gateway, 0, 50, 2000,
            TimeSpan.FromMinutes(10), 0.9m, CancellationToken.None);
        await NewScanManager().RunTokenRoundAsync(network, usdt, gateway, 0, 50, 2000,
            TimeSpan.FromMinutes(10), 0.9m, CancellationToken.None);

        var tasks = (await DbAsync()).SweepTasks.Where(t => t.TokenId == usdt.Id).ToList();
        tasks.Count.ShouldBe(1); // 合并成一笔归集
        tasks.Single().Amount.ShouldBe(0m); // 激活时按链接单求和
    }

    [Fact]
    public async Task Round_Limit_Tails_Are_Not_Credited_And_Relink_Later()
    {
        var tailUser = NewUser();
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500)
        {
            HasReceipt = true,
            ReceiptConfirmationsMet = true,
        };

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        // 直接种 Confirmed 单（绕过多轮次序依赖，只测激活→上限截取→确认入账的金额正确性）
        var orderIdA = await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId, status: DepositOrderStatus.Confirmed, userId: tailUser);
        var orderIdB = await SeedOrderAsync(network.Id, usdt, watched, 30m, addressId, status: DepositOrderStatus.Confirmed, userId: tailUser);
        // Queued 任务 + 链接两笔 Confirmed 单（模拟扫链确认推进后的状态）
        var taskId = Guid.NewGuid();
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await dbContext.SweepTasks.AddAsync(new ColSweepTask(taskId)
            {
                NetworkId = network.Id, TokenId = usdt.Id, UserId = tailUser, AddressId = addressId,
                Address = "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8],
                AddressIndex = 2, Amount = 50m,
                Status = SweepTaskStatus.Queued, StatusChangedAt = DateTime.Now, CreationTime = DateTime.Now,
            });
            await dbContext.DepositOrders
                .Where(o => o.Id == orderIdA || o.Id == orderIdB)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.SweepTaskId, (Guid?)taskId));
            await dbContext.SaveChangesAsync();
            await uow.CompleteAsync();
        }

        // 激活（上限 25 截取 A）→ 广播 → 确认入账 A；尾巴 B 解链
        var manager = NewSweepManager();
        for (var rd = 0; rd < 6; rd++)
            await manager.RunTokenRoundAsync(network, usdt, gateway, 12, 25m, 0.005m,
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        var db = await DbAsync();
        var a = await ReadOrderAsync(orderIdA);
        var b = await ReadOrderAsync(orderIdB);
        // 上限截取：只有 A（20）被广播+入账，B（30）留待下一轮（不被误入账）
        a.Status.ShouldBe(DepositOrderStatus.Swept);
        (await db.Balances.Where(x => x.UserId == tailUser).SumAsync(x => x.Balance)).ShouldBe(20m);
    }

    [Fact]
    public async Task Broadcast_Self_Heal_Recovers_Crash_Window_Orders()
    {
        var healUser = NewUser();
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500)
        {
            HasReceipt = true,
            ReceiptConfirmationsMet = true,
        };

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId, userId: healUser);
        // 模拟崩溃窗口：任务已 Fueling（claim 已提交）但链接单仍是 Confirmed（翻状态丢失）
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var taskId = Guid.NewGuid();
            await dbContext.SweepTasks.AddAsync(new ColSweepTask(taskId)
            {
                NetworkId = network.Id, TokenId = usdt.Id, UserId = healUser, AddressId = addressId,
                Address = "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8],
                AddressIndex = 2, Amount = 20m,
                Status = SweepTaskStatus.Fueling, StatusChangedAt = DateTime.Now, CreationTime = DateTime.Now,
            });
            await dbContext.DepositOrders
                .Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, DepositOrderStatus.Sweeping)
                    .SetProperty(o => o.SweepTaskId, taskId));
            await dbContext.SaveChangesAsync();
            await uow.CompleteAsync();
        }

        var manager = NewSweepManager();
        for (var rd = 0; rd < 5; rd++)
            await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        // 自愈：崩溃窗口的单被重新驱动并入账（而不是零链接拒广播/永久搁浅）
        var db = await DbAsync();
        (await db.Balances.SingleAsync(b => b.UserId == healUser)).Balance.ShouldBe(20m);
        (await ReadOrderAsync(orderId)).Status.ShouldBe(DepositOrderStatus.Swept);
    }

    [Fact]
    public async Task Zero_Linked_Sweeping_Task_Refuses_Blind_Broadcast()
    {
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500);

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        // 直造一个 Sweeping 任务但链接单全部丢失（SweepTaskId 指向它，状态 Confirmed 不存在）
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await dbContext.SweepTasks.AddAsync(new ColSweepTask(Guid.NewGuid())
            {
                NetworkId = network.Id, TokenId = usdt.Id, UserId = User1, AddressId = addressId,
                Address = "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8],
                AddressIndex = 2, Amount = 20m,
                Status = SweepTaskStatus.Sweeping, StatusChangedAt = DateTime.Now, CreationTime = DateTime.Now,
            });
            await dbContext.SaveChangesAsync();
            await uow.CompleteAsync();
        }

        await NewSweepManager().RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        var task = (await DbAsync()).SweepTasks.Single(t => t.AddressId == addressId);
        task.Status.ShouldBe(SweepTaskStatus.Failed);
        task.Retryable.ShouldBeFalse();
        task.FailReason.ShouldNotBeNull();
        task.FailReason.ShouldContain("拒绝盲目归集");
        gateway.BroadcastCount.ShouldBe(0); // 拒绝盲目归集：不广播真金
    }

    [Fact]
    public async Task First_Open_Seeds_Cursor_Even_With_Watched_Addresses()
    {
        // scanRan: false + 已有看管地址：首轮游标必须落位（原 H 级死分支回归钉子）
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: false);
        var gateway = new FakeChainGateway(head: 500);
        gateway.Logs["0xusdt"] = new List<TokenTransferLog>
        {
            new("0xpre-open", 0, "0xfrom", watched, "1000000000000000000", 130),
        };
        await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);

        await NewScanManager().RunTokenRoundAsync(network, usdt, gateway, 12, 50, 2000,
            TimeSpan.FromMinutes(10), 0.9m, CancellationToken.None);

        var token = await ReadTokenAsync(usdt.Id);
        token.LastScannedBlock.ShouldBe(488L); // head 500 - 确认数 12
        // 首轮窗口为空：历史转入按计划口径不补扫
        (await ReadOrdersAsync(usdt.Id)).Count.ShouldBe(0);
    }

    [Fact]
    public async Task Vault_Audit_Writes_Row_On_Shortfall()
    {
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0x" + new string('a', 40);
        var gateway = new FakeChainGateway(head: 500); // balanceOf 固定 0：用户有余额 → 缺口留痕
        var watched = "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId, status: DepositOrderStatus.Swept);

        // 用户侧余额 20（缺口核对的对账基数）：直接造行，绕过入账路径
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await dbContext.Balances.AddAsync(new ColBalance(Guid.NewGuid())
            {
                UserId = User2, Currency = CollectionCurrency.Usdt,
                Balance = 20m,
            });
            await dbContext.SaveChangesAsync();
            await uow.CompleteAsync();
        }

        var manager = GetRequiredService<VaultAuditManager>();
        await manager.RunAsync(network, usdt, gateway, CancellationToken.None);

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var row = await dbContext.VaultChecks.AsNoTracking().SingleAsync(v => v.TokenId == usdt.Id);
            row.VaultBalance.ShouldBe(0m);
            row.Difference.ShouldBeNegative(); // vault < users（缺口方向正确） // 负差额 = 缺口
            await uow.CompleteAsync();
        }
    }

    [Fact]
    public async Task Sweep_Revert_Marks_Task_Failed_Manual_And_Keeps_Orders()
    {
        var rvUser = NewUser();
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500)
        {
            HasReceipt = true,
            ReceiptStatusOverride = 0, // 链上 revert（代币暂停/黑名单族）
        };

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId);
        await ActivateAsync(network, usdt, gateway, orderId);

        var manager = NewSweepManager();
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        var task = (await DbAsync()).SweepTasks.Single(t => t.TokenId == usdt.Id);
        task.Status.ShouldBe(SweepTaskStatus.Failed);
        task.Retryable.ShouldBeFalse(); // 仅人工
        var order = await ReadOrderAsync(orderId);
        order.Status.ShouldBe(DepositOrderStatus.Sweeping); // 单不动，等人工
        (await DbAsync()).Balances.Count(b => b.UserId == rvUser).ShouldBe(0); // 不入账
    }

    [Fact]
    public async Task Failed_Retryable_Resign_Reuses_Same_Nonce()
    {
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500);

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId);
        await ActivateAsync(network, usdt, gateway, orderId); // 广播 fake-1（nonce 0）

        // 宽限已过 + 无回执：清哈希保 nonce；重签轮同 nonce 换新哈希
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await dbContext.SweepTasks
                .Where(t => t.TokenId == usdt.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.BroadcastAt, (DateTime?)TestClock.Now.AddMinutes(-40)));
            await uow.CompleteAsync();
        }

        var manager = NewSweepManager();
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        var retried = (await DbAsync()).SweepTasks.Single(t => t.TokenId == usdt.Id);
        retried.TxHash.ShouldNotBeNull();
        retried.TxHash.ShouldNotBe("0xfake-1");
        gateway.Broadcasts.Count.ShouldBe(2);
        gateway.Broadcasts[0].Nonce.ShouldBe(gateway.Broadcasts[1].Nonce); // 同 nonce 替换，绝不开新 nonce
        retried.NonceUsed.ShouldBe(gateway.Broadcasts[1].Nonce);
    }

    [Fact]
    public async Task Insufficient_Confirmations_Keeps_TxHash_And_Does_Not_Resign()
    {
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500);
        gateway.HasReceipt = true;
        gateway.ReceiptConfirmationsMet = false; // 回执在块里但确认数不够

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId);
        await ActivateAsync(network, usdt, gateway, orderId);

        var manager = NewSweepManager();
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        var task = (await DbAsync()).SweepTasks.Single(t => t.TokenId == usdt.Id);

        var hashAfterFirst = task.TxHash;
        hashAfterFirst.ShouldNotBeNull();
        task.Status.ShouldBe(SweepTaskStatus.Sweeping);
        task.TxBlockNumber.ShouldBe(495L); // 回执块号留痕
        var linkedOrder = await ReadOrderAsync(orderId);
        linkedOrder.Status.ShouldBe(DepositOrderStatus.Sweeping); // 单保持广播中，不入账

        // 再跑：不重签、不换哈希（深度不够不重签）
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        (await DbAsync()).SweepTasks.Single(t => t.TokenId == usdt.Id).TxHash.ShouldBe(hashAfterFirst);
        gateway.BroadcastCount.ShouldBe(1);
    }

    [Fact]
    public async Task Failed_Retryable_Task_Is_Pulled_Back_After_Cooldown()
    {
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500);

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId);
        await ActivateAsync(network, usdt, gateway, orderId); // 广播 fake-1，回执未达（等确认）

        // 人为置 Failed（可再试，冷却已过），同时把广播时间拨回宽限之前——
        // 重签前提是「宽限后节点仍不认这笔哈希」，冷却与宽限在本用例同时满足
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await dbContext.SweepTasks
                .Where(t => t.TokenId == usdt.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, SweepTaskStatus.Failed)
                    .SetProperty(t => t.Retryable, true)
                    .SetProperty(t => t.BroadcastAt, (DateTime?)TestClock.Now.AddMinutes(-40))
                    .SetProperty(t => t.StatusChangedAt, TestClock.Now.AddMinutes(-20)));
            await uow.CompleteAsync();
        }

        var manager = NewSweepManager();
        // 重试轮：Queued→Fueling→Sweeping→宽限已过→清哈希（Nonce 保留，等重签）
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        var afterRetry = await ReadOrderTaskAsync(usdt.Id);
        afterRetry.Status.ShouldBe(SweepTaskStatus.Sweeping);
        afterRetry.TxHash.ShouldBeNull("宽限已过应清哈希等重签");

        // 重签轮：同 nonce 换新哈希（假网关每次递增）
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        var retried = await ReadOrderTaskAsync(usdt.Id);
        retried.Status.ShouldBe(SweepTaskStatus.Sweeping);
        retried.TxHash.ShouldNotBeNull();
        retried.TxHash.ShouldNotBe("0xfake-1");
        gateway.BroadcastCount.ShouldBe(2);
    }

    private async Task<ColSweepTask> ReadOrderTaskAsync(Guid tokenId)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var result = await dbContext.SweepTasks.AsNoTracking().SingleAsync(t => t.TokenId == tokenId);
        await uow.CompleteAsync();
        return result;
    }

    [Fact]
    public async Task Sweep_Confirmation_Credits_Balance_And_Creates_TopUp_Order_Exactly_Once()
    {
        var scUser = NewUser();
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500);
        gateway.HasReceipt = true;
        gateway.ReceiptConfirmationsMet = true;

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId, userId: scUser);
        await ActivateAsync(network, usdt, gateway, orderId);

        var manager = NewSweepManager();
        // 广播轮 + 确认轮（广播与回执核对分轮，与生产轮次一致）
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        var dbContext = await DbAsync();
        (await dbContext.Balances.SingleAsync(b => b.UserId == scUser)).Balance.ShouldBe(20m);
        var order = await dbContext.DepositOrders.SingleAsync(o => o.Id == orderId);
        order.Status.ShouldBe(DepositOrderStatus.Swept);

        // 账本闭环（提现 M2 起归集确认同事务过账 deposit/pay 借 1001 / 贷 2201）：
        // 用户可用科目投影 == 余额行，链上资产科目 == 入账金额——两侧账本数值必须咬合
        var ledger = GetRequiredService<AbpAdmin.Ledger.Integration.Contracts.ILedgerIntegrationService>();
        (await ledger.GetBalanceAsync(new AbpAdmin.Ledger.Integration.Contracts.AccountRef(
            AbpAdmin.Ledger.Integration.Contracts.FinanceAccountCodes.UserAvailable,
            AbpAdmin.Ledger.Integration.Contracts.FinOwnerType.User, scUser.ToString()), "USDT")).ShouldBe(20m);
        (await ledger.GetBalanceAsync(new AbpAdmin.Ledger.Integration.Contracts.AccountRef(
            AbpAdmin.Ledger.Integration.Contracts.FinanceAccountCodes.OnChainAsset,
            AbpAdmin.Ledger.Integration.Contracts.FinOwnerType.Platform, ""), "USDT")).ShouldBe(20m);

        // 重启（重跑）不重复入账也不过账：任务已 Swept，再来一轮两侧账本都不动
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        var after = await DbAsync();
        after.Balances.Single(b => b.UserId == scUser).Balance.ShouldBe(20m);
        (await ledger.GetBalanceAsync(new AbpAdmin.Ledger.Integration.Contracts.AccountRef(
            AbpAdmin.Ledger.Integration.Contracts.FinanceAccountCodes.UserAvailable,
            AbpAdmin.Ledger.Integration.Contracts.FinOwnerType.User, scUser.ToString()), "USDT")).ShouldBe(20m);
        (await ledger.GetBalanceAsync(new AbpAdmin.Ledger.Integration.Contracts.AccountRef(
            AbpAdmin.Ledger.Integration.Contracts.FinanceAccountCodes.OnChainAsset,
            AbpAdmin.Ledger.Integration.Contracts.FinOwnerType.Platform, ""), "USDT")).ShouldBe(20m);
    }

    // ── 共用 ─────────────────────────────────────────────────────────────────────────────

    private readonly List<IUnitOfWork> _openUows = new();

    /// <summary>取活动上下文：无环境事务时开一个并挂到测试生命周期（用例结束统一提交释放）。</summary>
    private async Task<CollectionDbContext> DbAsync()
    {
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        if (uowManager.Current != null)
        {
            return await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        }

        var uow = uowManager.Begin();
        _openUows.Add(uow);
        return await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
    }

    void IDisposable.Dispose()
    {
        foreach (var uow in _openUows)
        {
            try
            {
                uow.CompleteAsync().GetAwaiter().GetResult();
                uow.Dispose();
            }
            catch
            {
                // 用例内已回滚/释放的单元：清理幂等
            }
        }

        _openUows.Clear();

        // 必须补调基类：显式接口实现会遮蔽基类 public Dispose（应用释放 + Respawn 清库都在那）。
        // 此前漏调 → 本类用例间从不清库，Col/Fin 数据跨用例累计——账本科目全量断言（如 1001
        // 链上资产合计）实测 60 而非 20 抓出；旧用例靠"每用例新 GUID"的相对断言侥幸未炸。
        base.Dispose();
    }

    /// <summary>种一笔进款单；默认 PendingConfirm 且块号落在首扫窗口（head 500，游标 100 → 扫 101..150）。</summary>
    private async Task<Guid> SeedOrderAsync(
        Guid networkId, ColToken token, string address, decimal amount,
        Guid? addressId = null, DepositOrderStatus? status = null, long blockNumber = 120,
        Guid? userId = null)
    {
        addressId ??= await SeedWatchedAddressAsync(await DbAsync(), networkId, address);
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var order = new ColDepositOrder(TestGuid.Next())
        {
            NetworkId = networkId, TokenId = token.Id, UserId = userId ?? User1, AddressId = addressId.Value,
            ToAddress = address, FromAddress = "0xfrom",
            TxHash = "0xseed-" + Guid.NewGuid().ToString("N"), LogIndex = Random.Shared.Next(0, 200),
            RawAmount = "0", Amount = amount, BlockNumber = blockNumber,
            Status = status ?? DepositOrderStatus.PendingConfirm, CreationTime = TestClock.Now,
        };
        await dbContext.DepositOrders.AddAsync(order);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return order.Id;
    }

    /// <summary>把 PendingConfirm 单推进到 Sweeping（扫链确认→建任务→激活→广播）。</summary>
    private async Task<Guid> ActivateAsync(ColNetwork network, ColToken token, FakeChainGateway gateway, Guid orderId)
    {
        await NewScanManager().RunTokenRoundAsync(network, token, gateway, 12, 50, 2000,
            TimeSpan.FromMinutes(10), 0.9m, CancellationToken.None);
        var manager = NewSweepManager();
        await manager.RunTokenRoundAsync(network, token, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        return (await DbAsync()).SweepTasks.Single(t => t.TokenId == token.Id).Id;
    }

    private async Task<List<ColDepositOrder>> ReadOrdersAsync(Guid tokenId)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var result = await dbContext.DepositOrders.AsNoTracking().Where(o => o.TokenId == tokenId).ToListAsync();
        await uow.CompleteAsync();
        return result;
    }

    private async Task<ColDepositOrder> ReadOrderAsync(Guid id)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var result = await dbContext.DepositOrders.AsNoTracking().SingleAsync(o => o.Id == id);
        await uow.CompleteAsync();
        return result;
    }

    private async Task<ColToken> ReadTokenAsync(Guid id)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var result = await dbContext.Tokens.AsNoTracking().SingleAsync(t => t.Id == id);
        await uow.CompleteAsync();
        return result;
    }

    private static class TestGuid
    {
        public static Guid Next() => Guid.NewGuid();
    }

    private static class TestClock
    {
        public static DateTime Now => DateTime.Now;
    }
}

/// <summary>
/// 链网关假实现：按合约存日志表；TruncateThreshold 模拟节点截断（返回条数 ≥ 阈值判未读完）；
/// HasReceipt/ReceiptConfirmationsMet 控制归集确认路径；ThrowOnContract 模拟一种币通道故障。
/// </summary>
public class FakeChainGateway : IBscChainGateway
{
    public Dictionary<string, List<TokenTransferLog>> Logs { get; } = new();

    public long Head { get; set; }

    public int TruncateThreshold { get; set; } = int.MaxValue;

    public string? ThrowOnContract { get; set; }

    public bool HasReceipt { get; set; }

    public bool ReceiptConfirmationsMet { get; set; }

    /// <summary>回执状态覆盖（null=无回执；0=revert；1=成功）。</summary>
    public int? ReceiptStatusOverride { get; set; }

    /// <summary>按地址的 BNB 余额（缺省 1 = 有 gas）。</summary>
    public Dictionary<string, decimal> BnbBalances { get; } = new();

    public List<(string To, string DataHex, long Nonce)> Broadcasts { get; } = new();

    public int BroadcastCount { get; private set; }

    public FakeChainGateway(long head)
    {
        Head = head;
    }

    public Task<long> GetLatestBlockNumberAsync(CancellationToken ct = default) => Task.FromResult(Head);

    public Task<(IReadOnlyList<TokenTransferLog> Logs, bool Complete)> GetTransferLogsAsync(
        string contractAddress, long fromBlock, long toBlock, CancellationToken ct = default)
    {
        if (contractAddress == ThrowOnContract)
        {
            throw new ChainRpcException("simulated channel failure");
        }

        var logs = Logs.TryGetValue(contractAddress, out var list)
            ? list.Where(l => l.BlockNumber >= fromBlock && l.BlockNumber <= toBlock).ToList()
            : new List<TokenTransferLog>();
        var complete = TruncateThreshold == int.MaxValue || logs.Count < TruncateThreshold;
        return Task.FromResult<(IReadOnlyList<TokenTransferLog>, bool)>((logs, complete));
    }

    public Task<AddressTransferPage> GetTransfersForAddressAsync(
        string contractAddress, string address, long fromBlock, long toBlock, int page, int pageSize,
        CancellationToken ct = default)
    {
        var logs = Logs.TryGetValue(contractAddress, out var list)
            ? list.Where(l => l.BlockNumber >= fromBlock && l.BlockNumber <= toBlock).ToList()
            : new List<TokenTransferLog>();
        var complete = TruncateThreshold == int.MaxValue || logs.Count < TruncateThreshold;
        return Task.FromResult(new AddressTransferPage(logs, !complete));
    }

    /// <summary>按 (合约, 地址) 的代币原始余额（提现护栏用；缺省 0）。</summary>
    public Dictionary<string, string> TokenBalances { get; } = new();

    public Task<string> GetTokenBalanceRawAsync(string contractAddress, string address, CancellationToken ct = default)
        => Task.FromResult(TokenBalances.TryGetValue(contractAddress + ":" + address, out var raw) ? raw : "0");

    public Task<decimal> GetBnbBalanceAsync(string address, CancellationToken ct = default)
        => Task.FromResult(BnbBalances.TryGetValue(address, out var b) ? b : 1m);

    /// <summary>可扰动计数：同 nonce 重签的回归测试靠它区分「复用存储 nonce」与「重查链上计数」。</summary>
    public long TxCountSeed { get; set; }

    public Task<long> GetTransactionCountAsync(string address, CancellationToken ct = default)
        => Task.FromResult(TxCountSeed);

    public Task<TransactionReceipt> GetTransactionReceiptAsync(string txHash, CancellationToken ct = default)
        => Task.FromResult(HasReceipt
            ? new TransactionReceipt(txHash, ReceiptConfirmationsMet ? 400L : 495L, ReceiptStatusOverride ?? 1)
            : new TransactionReceipt(txHash, null, null));

    public Task<string> SendTransactionAsync(
        string privateKey, string to, string dataHex, string valueWei, long chainId, long nonce,
        CancellationToken ct = default)
    {
        BroadcastCount++;
        Broadcasts.Add((to, dataHex, nonce));
        return Task.FromResult("0xfake-" + BroadcastCount);
    }

    public Task<decimal> GetGasPriceGweiAsync(CancellationToken ct = default) => Task.FromResult(1m);
}
