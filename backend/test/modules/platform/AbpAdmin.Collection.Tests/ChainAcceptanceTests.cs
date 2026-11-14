using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Jobs;
using AbpAdmin.Collection.Services;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp;
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

    /// <summary>资金路径用例各自独立用户：共享 PG 库会跨用例串台。</summary>
    private static Guid NewUser() => Guid.NewGuid();
    private static readonly Guid User2 = Guid.Parse("69696969-6969-6969-6969-696969696969");

    /// <summary>每用例唯一地址：用例共享同一 PG 库，固定地址会跨用例串行。</summary>
    private static string WatchAddress => "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8];

    /// <summary>落库设置网络充值费率/自动归集额度（RunTokenRound 会重读网络行，内存改动无效）。</summary>
    private async Task SetNetworkDepositPolicyAsync(Guid networkId, decimal feeRate, decimal autoMax)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        await dbContext.Networks.Where(n => n.Id == networkId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.DepositFeeRate, feeRate)
                .SetProperty(n => n.AutoCreditMaxAmount, autoMax));
        await uow.CompleteAsync();
    }

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
            AutoCreditMaxAmount = 5000m, // 大额放行门默认（与生产种子同值；0=全拦哨兵）
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
            SimulatedSweepSender = watched,
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
                Address = watched,
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
        // 上限截取：只有 A（20）被广播+入账，B（30）不会提前入账。
        a.Status.ShouldBe(DepositOrderStatus.Swept);
        b.Status.ShouldBe(DepositOrderStatus.Confirmed);
        b.SweepTaskId.ShouldBeNull();
        (await db.Balances.Where(x => x.UserId == tailUser).SumAsync(x => x.Balance)).ShouldBe(20m);
    }

    [Fact]
    public async Task Manually_Released_Tail_Is_Enqueued_Again_Above_Automatic_Limit()
    {
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        await SetNetworkDepositPolicyAsync(network.Id, 0m, 25m);
        usdt.VaultAddress = "0x" + new string('a', 40);
        var watched = WatchAddress;
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var first = await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId, status: DepositOrderStatus.Confirmed);
        var tail = await SeedOrderAsync(network.Id, usdt, watched, 100m, addressId, status: DepositOrderStatus.Confirmed);
        var gateway = new FakeChainGateway(head: 500);
        var scanner = NewScanManager();
        await scanner.RunTokenRoundAsync(network, usdt, gateway, 12, 50, 1440,
            TimeSpan.FromMinutes(1440), 0.9m, CancellationToken.None);
        await GetRequiredService<CollectionNetworkAdminAppService>().ApproveLargeDepositAsync(tail);
        var originalTask = (await ReadOrderAsync(first)).SweepTaskId;
        (await ReadOrderAsync(tail)).SweepTaskId.ShouldBe(originalTask);

        await NewSweepManager().RunTokenRoundAsync(network, usdt, gateway, 12, 25m, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        var detached = await ReadOrderAsync(tail);
        detached.SweepTaskId.ShouldBeNull();
        detached.ManuallyReleased.ShouldBeTrue();

        await scanner.RunTokenRoundAsync(network, usdt, gateway, 12, 50, 1440,
            TimeSpan.FromMinutes(1440), 0.9m, CancellationToken.None);
        var linked = await ReadOrderAsync(tail);
        linked.SweepTaskId.ShouldNotBeNull();
        linked.SweepTaskId.ShouldNotBe(originalTask);
        (await DbAsync()).SweepTasks.Count(t => t.AddressId == addressId).ShouldBe(2);
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
            SimulatedSweepSender = watched,
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
                Address = watched,
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
        task.FailReason.ShouldContain("拒绝广播");
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
        (await ReadOrderTaskAsync(usdt.Id)).Status.ShouldBe(SweepTaskStatus.Sweeping);
        gateway.ReceiptConfirmationsMet = true;
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        var task = (await DbAsync()).SweepTasks.Single(t => t.TokenId == usdt.Id);
        task.Status.ShouldBe(SweepTaskStatus.Failed);
        task.Retryable.ShouldBeFalse(); // 仅人工
        task.TxHash.ShouldBeNull();
        task.NonceUsed.ShouldBeNull();
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

        // 宽限已过且无回执：重签复用已保存的 nonce。
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

    [Theory]
    [InlineData(ReplacementReceipt.Shallow)]
    [InlineData(ReplacementReceipt.Malformed)]
    [InlineData(ReplacementReceipt.TimedOut)]
    [InlineData(ReplacementReceipt.IncompleteTransferProof)]
    public async Task Original_Hash_Can_Win_After_A_Replacement_And_Vault_Change(ReplacementReceipt replacementReceipt)
    {
        var user = NewUser();
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        var vault = "0x" + new string('a', 40);
        token.VaultAddress = vault;
        var watched = WatchAddress;
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, token, watched, 20m, addressId, userId: user);
        var gateway = new FakeChainGateway(head: 500) { SimulatedSweepSender = watched };
        await ActivateAsync(network, token, gateway, orderId);
        var original = await ReadOrderTaskAsync(token.Id);
        original.BroadcastVaultAddress.ShouldBe(vault);
        var originalHash = original.TxHash;
        originalHash.ShouldNotBeNull();

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.SweepTasks.Where(t => t.Id == original.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BroadcastAt, TestClock.Now.AddMinutes(-40)));
            await uow.CompleteAsync();
        }

        token.VaultAddress = "0x" + new string('b', 40);
        var manager = NewSweepManager();
        async Task RoundAsync()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await manager.RunTokenRoundAsync(network, token, gateway, 12, null, 0.005m,
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, deadline.Token);
        }
        await RoundAsync();
        var replacement = await ReadOrderTaskAsync(token.Id);
        replacement.TxHash.ShouldNotBeNull();
        replacement.TxHash.ShouldNotBe(originalHash);
        replacement.PreviousTxHashes.ShouldNotBeNull();
        replacement.PreviousTxHashes.ShouldContain(originalHash);
        gateway.Broadcasts.Count.ShouldBe(2);
        gateway.Broadcasts.Select(call => call.Nonce).ShouldBe(new[] { original.NonceUsed!.Value, original.NonceUsed.Value });

        gateway.HasReceipt = true;
        gateway.ReceiptHashes = [originalHash, replacement.TxHash];
        if (replacementReceipt == ReplacementReceipt.IncompleteTransferProof)
        {
            gateway.IncompleteReceiptLogHashes.Add(replacement.TxHash);
        }
        else
        {
            gateway.ShallowReceiptHashes = [replacement.TxHash];
        }
        gateway.ReceiptConfirmationsMet = true;
        var receiptReads = new List<string>();
        var candidateCanceled = false;
        gateway.BeforeCancellableReceiptRead = async (hash, ct) =>
        {
            receiptReads.Add(hash);
            if (hash != replacement.TxHash)
            {
                return;
            }
            if (replacementReceipt == ReplacementReceipt.Malformed)
            {
                throw new ChainRpcException("simulated malformed receipt");
            }
            if (replacementReceipt == ReplacementReceipt.TimedOut)
            {
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                finally
                {
                    candidateCanceled = ct.IsCancellationRequested;
                }
            }
        };
        await RoundAsync();
        await RoundAsync();
        candidateCanceled.ShouldBe(replacementReceipt == ReplacementReceipt.TimedOut);
        receiptReads.ShouldBe([replacement.TxHash, originalHash]);
        if (replacementReceipt == ReplacementReceipt.IncompleteTransferProof)
        {
            gateway.ReceiptLogReads.ShouldBe([replacement.TxHash, originalHash]);
        }
        var settled = await ReadOrderTaskAsync(token.Id);
        settled.TxHash.ShouldBe(originalHash);
        (settled.PreviousTxHashes ?? []).ShouldBeEmpty();
        settled.Status.ShouldBe(SweepTaskStatus.Swept);
        (await ReadOrderAsync(orderId)).Status.ShouldBe(DepositOrderStatus.Swept);
        gateway.BroadcastCount.ShouldBe(2);
        await AssertSweepCreditAsync(user, orderId, expected: true);
    }

    public enum ReplacementReceipt
    {
        Shallow,
        Malformed,
        TimedOut,
        IncompleteTransferProof,
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Untrusted_Sweep_Receipt_Remains_Pending_Past_Grace_Without_Rebroadcast(bool malformedResponse)
    {
        var user = NewUser();
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        token.VaultAddress = "0x" + new string('a', 40);
        var watched = WatchAddress;
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, token, watched, 20m, addressId, userId: user);
        var gateway = new FakeChainGateway(head: 500);
        await ActivateAsync(network, token, gateway, orderId);
        var sent = gateway.Broadcasts.Single();
        var broadcastAt = DateTime.UtcNow.AddMinutes(-40);

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.SweepTasks.Where(t => t.TokenId == token.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BroadcastAt, broadcastAt));
            await uow.CompleteAsync();
        }

        gateway.HasReceipt = true;
        gateway.ReceiptConfirmationsMet = true;
        gateway.ReceiptStatusOverride = 2;
        var receiptReads = new List<string>();
        gateway.BeforeReceiptRead = hash =>
        {
            receiptReads.Add(hash);
            return malformedResponse
                ? Task.FromException(new ChainRpcException("simulated malformed receipt"))
                : Task.CompletedTask;
        };
        var manager = NewSweepManager();
        for (var round = 0; round < 2; round++)
        {
            await manager.RunTokenRoundAsync(network, token, gateway, 12, null, 0.005m,
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        }

        receiptReads.ShouldBe([sent.Hash, sent.Hash]);
        gateway.Broadcasts.Single().Hash.ShouldBe(sent.Hash);
        var pending = await ReadOrderTaskAsync(token.Id);
        pending.Status.ShouldBe(SweepTaskStatus.Sweeping);
        pending.TxHash.ShouldBe(sent.Hash);
        pending.NonceUsed.ShouldBe(sent.Nonce);
        pending.BroadcastAt.ShouldNotBeNull().ShouldBe(broadcastAt, TimeSpan.FromMilliseconds(1));
        pending.TxBlockNumber.ShouldBeNull();
        (await ReadOrderAsync(orderId)).Status.ShouldBe(DepositOrderStatus.Sweeping);
        await AssertSweepCreditAsync(user, orderId, expected: false);
    }

    [Fact]
    public async Task Confirmed_Sweep_Settles_Before_Working_Through_A_Large_Queue()
    {
        const int backlogSize = 256;
        var user = NewUser();
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        token.VaultAddress = "0x" + new string('a', 40);
        var watched = WatchAddress;
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, token, watched, 20m, addressId, userId: user);
        var gateway = new FakeChainGateway(head: 500) { SimulatedSweepSender = watched };
        await ActivateAsync(network, token, gateway, orderId);
        var originalHash = gateway.Broadcasts.Single().Hash;
        gateway.HasReceipt = true;
        gateway.ReceiptHashes = [originalHash];
        gateway.ReceiptConfirmationsMet = true;

        await SeedSweepBacklogAsync(token, backlogSize, DateTime.UtcNow);

        int? queuedAtReceipt = null;
        gateway.BeforeReceiptRead = async hash =>
        {
            if (hash == originalHash)
            {
                queuedAtReceipt = await CountQueuedAsync();
            }
        };

        await NewSweepManager().RunTokenRoundAsync(network, token, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        queuedAtReceipt.ShouldBe(backlogSize);
        (await ReadOrderAsync(orderId)).Status.ShouldBe(DepositOrderStatus.Swept);
        (await CountQueuedAsync()).ShouldBe(backlogSize - ChainDefaults.SweepTaskBatchSize);
        await AssertSweepCreditAsync(user, orderId, expected: true);

        async Task<int> CountQueuedAsync()
        {
            using var read = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var count = await db.SweepTasks.CountAsync(t => t.TokenId == token.Id && t.Status == SweepTaskStatus.Queued);
            await read.CompleteAsync();
            return count;
        }
    }

    [Fact]
    public async Task Sweep_Receipt_Batch_Rotates_Past_Unresolved_Older_Attempts()
    {
        const int receiptBatchSize = ChainDefaults.SweepTaskBatchSize;
        var user = NewUser();
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        token.VaultAddress = "0x" + new string('a', 40);
        var watched = WatchAddress;
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, token, watched, 20m, addressId, userId: user);
        var gateway = new FakeChainGateway(head: 500) { SimulatedSweepSender = watched };
        await ActivateAsync(network, token, gateway, orderId);
        var targetHash = gateway.Broadcasts.Single().Hash;
        gateway.HasReceipt = true;
        gateway.ReceiptHashes = [targetHash];
        gateway.ReceiptConfirmationsMet = true;
        await SeedSweepBacklogAsync(token, receiptBatchSize, DateTime.UtcNow.AddHours(-1), withBroadcast: true);

        var queried = new List<string>();
        gateway.BeforeReceiptRead = hash =>
        {
            queried.Add(hash);
            return hash == targetHash ? Task.CompletedTask : Task.FromException(new ChainRpcException("simulated malformed receipt"));
        };

        await RoundAsync();
        queried.Count.ShouldBe(receiptBatchSize);
        queried.Distinct().Count().ShouldBe(receiptBatchSize);
        queried.ShouldNotContain(targetHash);
        (await ReadOrderAsync(orderId)).Status.ShouldBe(DepositOrderStatus.Sweeping);

        queried.Clear();
        await RoundAsync();
        queried.First().ShouldBe(targetHash);
        (await ReadOrderAsync(orderId)).Status.ShouldBe(DepositOrderStatus.Swept);
        gateway.Broadcasts.Single().Hash.ShouldBe(targetHash);
        await AssertSweepCreditAsync(user, orderId, expected: true);

        Task RoundAsync() => NewSweepManager().RunTokenRoundAsync(network, token, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
    }

    [Fact]
    public async Task Gas_Send_With_Unknown_Result_Reuses_Saved_Nonce()
    {
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        token.VaultAddress = "0x" + new string('a', 40);
        var watched = WatchAddress;
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, token, watched, 20m, addressId);
        var gateway = new FakeChainGateway(head: 500) { TxCountSeed = 61 };
        gateway.BnbBalances[watched] = 0m;
        gateway.AfterBroadcast = (_, _, _) => throw new ChainRpcException("gas response lost");
        await ActivateAsync(network, token, gateway, orderId);
        (await ReadOrderTaskAsync(token.Id)).GasNonceUsed.ShouldBe(61);
        gateway.AfterBroadcast = null;
        gateway.TxCountSeed = 90;
        await NewSweepManager().RunTokenRoundAsync(network, token, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        gateway.Broadcasts.Select(call => call.Nonce).ShouldBe(new long[] { 61, 61 });
        (await ReadOrderTaskAsync(token.Id)).GasTxHash.ShouldNotBeNull();
    }

    [Fact]
    public async Task Sweep_Lost_Send_Response_Settles_Original_Hash_Once_After_Restart()
    {
        var user = NewUser();
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        token.VaultAddress = "0x" + new string('a', 40);
        var watched = WatchAddress;
        var gateway = new FakeChainGateway(head: 500) { TxCountSeed = 41, SimulatedSweepSender = watched };
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, token, watched, 20m, addressId, userId: user);
        (string To, string Data, long Nonce, ColSweepTask Task)? broadcast = null;
        gateway.AfterBroadcast = async (to, data, nonce) =>
        {
            using var read = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var saved = await db.SweepTasks.AsNoTracking().SingleAsync(t => t.TokenId == token.Id);
            await read.CompleteAsync();
            broadcast = (to, data, nonce, saved);
            throw new ChainRpcException("the node accepted the broadcast but the response was lost");
        };

        await ActivateAsync(network, token, gateway, orderId);
        broadcast.HasValue.ShouldBeTrue();
        var observed = broadcast.GetValueOrDefault();
        observed.To.ShouldBe(token.ContractAddress);
        observed.Data.ShouldStartWith("0xa9059cbb" + new string('0', 24) + new string('a', 40));
        observed.Nonce.ShouldBe(41);
        observed.Task.NonceUsed.ShouldBe(41);
        observed.Task.BroadcastAt.ShouldNotBeNull();
        gateway.Broadcasts.Count.ShouldBe(1);
        var originalHash = gateway.Broadcasts.Single().Hash;
        observed.Task.TxHash.ShouldBe(originalHash);
        (await ReadOrderTaskAsync(token.Id)).TxHash.ShouldBe(originalHash);
        (await ReadOrderAsync(orderId)).Status.ShouldBe(DepositOrderStatus.Sweeping);
        var beforeSettlement = await DbAsync();
        (await beforeSettlement.Balances.CountAsync(row => row.UserId == user)).ShouldBe(0);

        gateway.AfterBroadcast = null;
        gateway.HasReceipt = true;
        gateway.ReceiptConfirmationsMet = true;
        gateway.ReceiptHashes = [originalHash];
        using var restartScope = ServiceProvider.CreateScope();
        var restarted = restartScope.ServiceProvider.GetRequiredService<SweepManager>().WithMasterKey("test-master-key");
        for (var round = 0; round < 2; round++)
        {
            await restarted.RunTokenRoundAsync(network, token, gateway, 12, null, 0.005m,
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        }

        gateway.BroadcastCount.ShouldBe(1);
        var settled = await ReadOrderTaskAsync(token.Id);
        settled.TxHash.ShouldBe(originalHash);
        (settled.PreviousTxHashes ?? []).ShouldBeEmpty();
        settled.Status.ShouldBe(SweepTaskStatus.Swept);
        (await ReadOrderAsync(orderId)).Status.ShouldBe(DepositOrderStatus.Swept);

        using var check = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var collection = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var ledger = await GetRequiredService<IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
        var depositKey = BalanceIdempotencyKeys.ForDepositOrder(orderId);
        (await collection.Balances.SingleAsync(row => row.UserId == user)).Balance.ShouldBe(20m);
        (await collection.BalanceTxns.CountAsync(row => row.UserId == user && row.IdempotencyKey == depositKey)).ShouldBe(1);
        (await ledger.Vouchers.CountAsync(row => row.BizRefId == depositKey && row.BizType == FinanceBizTypes.Deposit
            && row.Phase == FinanceBizTypes.Phases.Pay)).ShouldBe(1);
        await check.CompleteAsync();
    }

    [Fact]
    public async Task Sweep_Interrupted_Before_Node_Acceptance_Reuses_Saved_Nonce_And_Credits_Once()
    {
        var user = NewUser();
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        token.VaultAddress = "0x" + new string('a', 40);
        var watched = WatchAddress;
        var gateway = new FakeChainGateway(head: 500) { TxCountSeed = 41, SimulatedSweepSender = watched };
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, token, watched, 20m, addressId, userId: user);
        var depositKey = BalanceIdempotencyKeys.ForDepositOrder(orderId);
        gateway.BeforeSend = () => throw new OperationCanceledException("worker stopped before the RPC accepted the transaction");

        await Should.ThrowAsync<OperationCanceledException>(() => ActivateAsync(network, token, gateway, orderId));

        var interrupted = await ReadOrderTaskAsync(token.Id);
        interrupted.Status.ShouldBe(SweepTaskStatus.Sweeping);
        interrupted.TxHash.ShouldNotBeNull();
        interrupted.NonceUsed.ShouldBe(41);
        interrupted.BroadcastAt.ShouldNotBeNull();
        gateway.Broadcasts.ShouldBeEmpty();
        (await ReadOrderAsync(orderId)).Status.ShouldBe(DepositOrderStatus.Sweeping);
        await AssertCreditAsync(expected: false);

        gateway.BeforeSend = null;
        gateway.TxCountSeed = 90;
        using var restartScope = ServiceProvider.CreateScope();
        var restarted = restartScope.ServiceProvider.GetRequiredService<SweepManager>().WithMasterKey("test-master-key");
        Task RunRestartedRoundAsync() => restarted.RunTokenRoundAsync(network, token, gateway,
            12, null, 0.005m, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120),
            0.02m, CancellationToken.None);
        await RunRestartedRoundAsync(); // 池中无回执，宽限内不发第二次。
        gateway.Broadcasts.ShouldBeEmpty();

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.SweepTasks.Where(row => row.Id == interrupted.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.BroadcastAt, TestClock.Now.AddMinutes(-40)));
            await uow.CompleteAsync();
        }

        await RunRestartedRoundAsync();
        gateway.Broadcasts.Count.ShouldBe(1);
        var sent = gateway.Broadcasts.Single();
        sent.Nonce.ShouldBe(41);
        var retry = await ReadOrderTaskAsync(token.Id);
        retry.TxHash.ShouldBe(sent.Hash);
        retry.TxHash.ShouldNotBe(interrupted.TxHash);
        retry.PreviousTxHashes.ShouldNotBeNull();
        retry.PreviousTxHashes.ShouldContain(interrupted.TxHash);
        await AssertCreditAsync(expected: false);

        gateway.HasReceipt = true;
        gateway.ReceiptConfirmationsMet = true;
        gateway.ReceiptHashes = [sent.Hash];
        await RunRestartedRoundAsync();
        await RunRestartedRoundAsync();

        gateway.Broadcasts.Count.ShouldBe(1);
        var completed = await ReadOrderTaskAsync(token.Id);
        completed.Status.ShouldBe(SweepTaskStatus.Swept);
        completed.TxHash.ShouldBe(sent.Hash);
        (completed.PreviousTxHashes ?? []).ShouldBeEmpty();
        (await ReadOrderAsync(orderId)).Status.ShouldBe(DepositOrderStatus.Swept);
        await AssertCreditAsync(expected: true);

        async Task AssertCreditAsync(bool expected)
        {
            using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var collection = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var ledger = await GetRequiredService<IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
            var balances = await collection.Balances.Where(row => row.UserId == user).ToListAsync();
            if (expected)
            {
                balances.Single().Balance.ShouldBe(20m);
            }
            else
            {
                balances.ShouldBeEmpty();
            }
            var count = expected ? 1 : 0;
            (await collection.BalanceTxns.CountAsync(row => row.UserId == user && row.IdempotencyKey == depositKey)).ShouldBe(count);
            (await ledger.Vouchers.CountAsync(row => row.BizRefId == depositKey && row.BizType == FinanceBizTypes.Deposit
                && row.Phase == FinanceBizTypes.Phases.Pay)).ShouldBe(count);
            await uow.CompleteAsync();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Sweep_Rejects_Credit_Without_Confirmed_Transfer_Proof(int receiptStatus)
    {
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        token.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500)
        {
            HasReceipt = true, ReceiptConfirmationsMet = true, ReceiptStatusOverride = receiptStatus,
        };
        var user = NewUser();
        var watched = WatchAddress;
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, token, watched, 20m, addressId);
        await ActivateAsync(network, token, gateway, orderId);

        var manager = NewSweepManager();
        await manager.RunTokenRoundAsync(network, token, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        var task = (await DbAsync()).SweepTasks.Single(t => t.TokenId == token.Id);
        task.Status.ShouldBe(receiptStatus == 1 ? SweepTaskStatus.Failed : SweepTaskStatus.Sweeping);
        if (receiptStatus == 1)
        {
            task.FailReason!.ShouldContain("未包含与本单匹配的代币转账");
        }
        (await ReadOrderAsync(orderId)).Status.ShouldBe(DepositOrderStatus.Sweeping);
        (await DbAsync()).Balances.Count(b => b.UserId == user).ShouldBe(0);
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
        // 重试轮：超宽限后同 nonce 替换，收到新哈希才覆盖旧哈希。
        await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        var afterRetry = await ReadOrderTaskAsync(usdt.Id);
        afterRetry.Status.ShouldBe(SweepTaskStatus.Sweeping);
        afterRetry.TxHash.ShouldNotBeNull();
        afterRetry.TxHash.ShouldNotBe("0xfake-1");
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

    private async Task AssertSweepCreditAsync(Guid userId, Guid orderId, bool expected)
    {
        using var read = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var collection = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var ledger = await GetRequiredService<IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
        var depositKey = BalanceIdempotencyKeys.ForDepositOrder(orderId);
        var balance = await collection.Balances.SingleOrDefaultAsync(row => row.UserId == userId);
        if (expected)
        {
            balance.ShouldNotBeNull().Balance.ShouldBe(20m);
        }
        else
        {
            balance.ShouldBeNull();
        }
        var count = expected ? 1 : 0;
        (await collection.BalanceTxns.CountAsync(row => row.UserId == userId && row.IdempotencyKey == depositKey)).ShouldBe(count);
        (await ledger.Vouchers.CountAsync(row => row.BizRefId == depositKey && row.BizType == FinanceBizTypes.Deposit
            && row.Phase == FinanceBizTypes.Phases.Pay)).ShouldBe(count);
        await read.CompleteAsync();
    }

    private async Task SeedSweepBacklogAsync(ColToken token, int count, DateTime createdAt, bool withBroadcast = false)
    {
        using var seed = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        for (var index = 0; index < count; index++)
        {
            var address = new ColDepositAddress(TestGuid.Next())
            {
                UserId = NewUser(), NetworkId = token.NetworkId, Address = WatchAddress,
                AddressIndex = 1000 + index, CreationTime = createdAt,
            };
            var task = new ColSweepTask(TestGuid.Next())
            {
                NetworkId = token.NetworkId, TokenId = token.Id, UserId = address.UserId, AddressId = address.Id,
                Address = address.Address, AddressIndex = address.AddressIndex, Amount = 20m,
                Status = withBroadcast ? SweepTaskStatus.Sweeping : SweepTaskStatus.Queued,
                TxHash = withBroadcast ? $"0x{index:x64}" : null,
                BroadcastAt = withBroadcast ? createdAt : null,
                NonceUsed = withBroadcast ? 41L : null,
                BroadcastVaultAddress = withBroadcast ? token.VaultAddress : null,
                CreationTime = createdAt, StatusChangedAt = createdAt,
            };
            db.DepositAddresses.Add(address);
            db.SweepTasks.Add(task);
            db.DepositOrders.Add(new ColDepositOrder(TestGuid.Next())
            {
                NetworkId = token.NetworkId, TokenId = token.Id, UserId = address.UserId, AddressId = address.Id,
                ToAddress = address.Address, FromAddress = "0xfrom", TxHash = $"0x{1000 + index:x64}",
                RawAmount = "20000000000000000000", Amount = 20m, BlockNumber = 120,
                Status = withBroadcast ? DepositOrderStatus.Sweeping : DepositOrderStatus.Confirmed,
                SweepTaskId = task.Id, CreationTime = createdAt,
            });
        }
        await seed.CompleteAsync();
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
        gateway.SimulatedSweepSender = watched;
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

    [Fact]
    public async Task Deposit_Fee_Snapshot_And_Net_Credit()
    {
        var scUser = NewUser();
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        await SetNetworkDepositPolicyAsync(network.Id, feeRate: 0.001m, autoMax: 5000m); // 千一充值手续费
        var gateway = new FakeChainGateway(head: 500);
        gateway.HasReceipt = true;
        gateway.SimulatedSweepSender = watched;
        gateway.ReceiptConfirmationsMet = true;

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId, userId: scUser);
        // 手工建单不走建单快照路径——补写快照列（快照属于扫描建单路径，此处模拟其产物）
        using (var suow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var sdb = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await sdb.DepositOrders.Where(o => o.Id == orderId).ExecuteUpdateAsync(x => x
                .SetProperty(o => o.FeeRateSnapshot, 0.001m)
                .SetProperty(o => o.Fee, 0.02m)
                .SetProperty(o => o.ActualAmount, 19.98m));
            await suow.CompleteAsync();
        }

        // 先走扫描确认挂队（SweepManager 不推确认），再归集两轮（广播 + 回执确认入账）+ 一轮重放
        await ActivateAsync(network, usdt, gateway, orderId);
        var manager = NewSweepManager();
        for (var i = 0; i < 3; i++)
        {
            await manager.RunTokenRoundAsync(network, usdt, gateway, 12, null, 0.005m,
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);
        }

        var dbContext = await DbAsync();
        var order = await dbContext.DepositOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        order.Status.ShouldBe(DepositOrderStatus.Swept);

        // 余额 = 净额 19.98（毛 20 − fee 0.02）；流水与 Eto 同净额
        (await dbContext.Balances.SingleAsync(b => b.UserId == scUser)).Balance.ShouldBe(19.98m);

        // 账本：deposit/pay 净额（借 1001 / 贷 2201 = 19.98）+ deposit/fee（借 2201 / 贷 6003 = 0.02）
        var ledger = GetRequiredService<AbpAdmin.Ledger.Integration.Contracts.ILedgerIntegrationService>();
        (await ledger.GetBalanceAsync(new AbpAdmin.Ledger.Integration.Contracts.AccountRef(
            AbpAdmin.Ledger.Integration.Contracts.FinanceAccountCodes.UserAvailable,
            AbpAdmin.Ledger.Integration.Contracts.FinOwnerType.User, scUser.ToString()), "USDT")).ShouldBe(19.98m);
        (await ledger.GetBalanceAsync(new AbpAdmin.Ledger.Integration.Contracts.AccountRef(
            AbpAdmin.Ledger.Integration.Contracts.FinanceAccountCodes.WithdrawFeeIncome,
            AbpAdmin.Ledger.Integration.Contracts.FinOwnerType.Platform, ""), "USDT")).ShouldBe(0.02m);
    }

    [Fact]
    public async Task Large_Deposit_Stays_Confirmed_Until_Approved()
    {
        var scUser = NewUser();
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        await SetNetworkDepositPolicyAsync(network.Id, feeRate: 0m, autoMax: 20m); // 超过 20 的进款等人放行
        var gateway = new FakeChainGateway(head: 500);

        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, usdt, watched, 25m, addressId, userId: scUser, blockNumber: 480);
        var admin = GetRequiredService<Services.CollectionNetworkAdminAppService>();
        var pendingError = await Should.ThrowAsync<BusinessException>(() => admin.ApproveLargeDepositAsync(orderId));
        pendingError.Code.ShouldBe(CollectionErrorCodes.DepositNotReleasable);

        var scan = GetRequiredService<DepositScanManager>();
        await scan.RunTokenRoundAsync(network, usdt, gateway, 12, 50, 1440,
            TimeSpan.FromMinutes(1440), 0.9m, CancellationToken.None);

        var dbContext = await DbAsync();
        var order = await dbContext.DepositOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        order.Status.ShouldBe(DepositOrderStatus.Confirmed);
        order.SweepTaskId.ShouldBeNull(); // 大额门：确认但不挂队

        // 状态卡持有敞口：Confirmed 且未挂任务的单计入 held 计数（放行前 1 笔 25）
        var adminSvc = GetRequiredService<Services.CollectionNetworkAdminAppService>();
        var statusBefore = await adminSvc.GetStatusAsync(network.Id);
        statusBefore.HeldDepositCount.ShouldBeGreaterThanOrEqualTo(1);

        // 放行仅入队；第二次请求应确认原链接仍然有效。
        await admin.ApproveLargeDepositAsync(orderId);
        var taskId = (await ReadOrderAsync(orderId)).SweepTaskId;
        taskId.ShouldNotBeNull();
        await admin.ApproveLargeDepositAsync(orderId);
        (await ReadOrderAsync(orderId)).SweepTaskId.ShouldBe(taskId);
    }

    [Fact]
    public async Task Concurrent_Large_Deposit_Releases_Produce_One_Queued_Task()
    {
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        var watched = WatchAddress;
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, token, watched, 6000m, addressId, DepositOrderStatus.Confirmed);

        Task Approve(IServiceProvider services) => services.GetRequiredService<Services.CollectionNetworkAdminAppService>()
            .ApproveLargeDepositAsync(orderId);

        await RunWithAddressLockAsync(addressId, beforeRelease: null, Approve, Approve, Approve);

        var db = await DbAsync();
        var task = await db.SweepTasks.AsNoTracking().SingleAsync(row => row.AddressId == addressId);
        task.Status.ShouldBe(SweepTaskStatus.Queued);
        (await ReadOrderAsync(orderId)).SweepTaskId.ShouldBe(task.Id);
    }

    [Fact]
    public async Task Scanner_And_Manual_Release_Share_One_Queued_Task()
    {
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        var watched = WatchAddress;
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var orderId = await SeedOrderAsync(network.Id, token, watched, 25m, addressId, DepositOrderStatus.Confirmed);
        var secondId = await SeedOrderAsync(network.Id, token, watched, 30m, addressId);
        var gateway = new FakeChainGateway(head: 500);

        await RunWithAddressLockAsync(addressId, beforeRelease: null,
            services => services.GetRequiredService<Services.CollectionNetworkAdminAppService>().ApproveLargeDepositAsync(orderId),
            services => services.GetRequiredService<DepositScanManager>().RunTokenRoundAsync(network, token, gateway,
                12, 50, 1440, TimeSpan.FromMinutes(1440), 0.9m, CancellationToken.None));

        var db = await DbAsync();
        var task = await db.SweepTasks.AsNoTracking().SingleAsync(row => row.AddressId == addressId);
        task.Status.ShouldBe(SweepTaskStatus.Queued);
        var orders = await ReadOrdersAsync(token.Id);
        orders.Select(row => row.Id).ShouldBe([orderId, secondId], ignoreOrder: true);
        orders.ShouldAllBe(row => row.Status == DepositOrderStatus.Confirmed && row.SweepTaskId == task.Id);
    }

    [Fact]
    public async Task Sweep_Activation_Reloads_Orders_Linked_Before_Lock_Release()
    {
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        token.VaultAddress = "0xvault";
        var watched = WatchAddress;
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var firstId = await SeedOrderAsync(network.Id, token, watched, 20m, addressId, DepositOrderStatus.Confirmed);
        var secondId = await SeedOrderAsync(network.Id, token, watched, 25m, addressId, DepositOrderStatus.Confirmed);
        var admin = GetRequiredService<Services.CollectionNetworkAdminAppService>();
        await admin.ApproveLargeDepositAsync(firstId);
        var taskId = (await ReadOrderAsync(firstId)).SweepTaskId!.Value;
        var gateway = new FakeChainGateway(head: 500);

        Task RunSweep(IServiceProvider services) => services.GetRequiredService<SweepManager>().WithMasterKey("test-master-key")
            .RunTokenRoundAsync(network, token, gateway, 12, null, 0.005m,
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        await RunWithAddressLockAsync(addressId,
            db => db.DepositOrders.Where(row => row.Id == secondId)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.SweepTaskId, taskId)),
            RunSweep);

        // 放行请求可能在归集已经激活后才重试；此时仍确认原来的任务。
        await admin.ApproveLargeDepositAsync(secondId);
        var db = await DbAsync();
        var task = await db.SweepTasks.AsNoTracking().SingleAsync(row => row.AddressId == addressId);
        task.Id.ShouldBe(taskId);
        task.Amount.ShouldBe(45m);
        task.Status.ShouldBe(SweepTaskStatus.Sweeping);
        gateway.BroadcastCount.ShouldBe(1);
        var orders = await ReadOrdersAsync(token.Id);
        orders.Select(row => row.Id).ShouldBe([firstId, secondId], ignoreOrder: true);
        orders.ShouldAllBe(row => row.Status == DepositOrderStatus.Sweeping && row.SweepTaskId == taskId);
    }

    [Fact]
    public async Task New_Deposit_Does_Not_Join_A_Retried_Already_Broadcast_Task()
    {
        var (network, token, _) = await SeedChainAsync(scanRan: true);
        var watched = WatchAddress;
        var db = await DbAsync();
        var addressId = await SeedWatchedAddressAsync(db, network.Id, watched);
        var firstId = await SeedOrderAsync(network.Id, token, watched, 20m, addressId, DepositOrderStatus.Sweeping);
        var secondId = await SeedOrderAsync(network.Id, token, watched, 25m, addressId, DepositOrderStatus.Confirmed);
        var retried = new ColSweepTask(Guid.NewGuid())
        {
            NetworkId = network.Id, TokenId = token.Id, UserId = User1, AddressId = addressId, Address = watched,
            Amount = 20m, Status = SweepTaskStatus.Queued, TxHash = "0xalready-broadcast", NonceUsed = 41,
            BroadcastAt = TestClock.Now, StatusChangedAt = TestClock.Now, CreationTime = TestClock.Now,
        };
        db.SweepTasks.Add(retried);
        await db.SaveChangesAsync();
        await db.DepositOrders.Where(row => row.Id == firstId)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.SweepTaskId, retried.Id));

        var admin = GetRequiredService<Services.CollectionNetworkAdminAppService>();
        await admin.ApproveLargeDepositAsync(secondId);
        var tasks = await db.SweepTasks.AsNoTracking().Where(row => row.AddressId == addressId).ToListAsync();
        tasks.Count.ShouldBe(2);
        var newTask = tasks.Single(row => row.Id != retried.Id);
        newTask.Status.ShouldBe(SweepTaskStatus.Queued);
        newTask.TxHash.ShouldBeNull();
        tasks.Single(row => row.Id == retried.Id).TxHash.ShouldBe(retried.TxHash);
        (await ReadOrderAsync(firstId)).SweepTaskId.ShouldBe(retried.Id);
        (await ReadOrderAsync(secondId)).SweepTaskId.ShouldBe(newTask.Id);
    }

    [Fact]
    public async Task Deposit_Gate_Boundary_Equality_AutoEnqueues_Negative_ClampsToHoldAll()
    {
        var scUser = NewUser();
        var watched = WatchAddress;
        var (network, usdt, _) = await SeedChainAsync(scanRan: true);
        usdt.VaultAddress = "0xvault";
        var gateway = new FakeChainGateway(head: 500);
        var scan = GetRequiredService<DepositScanManager>();

        // 边界等值：毛额 == 额度 → 严格 > 判定 → 自动入队
        await SetNetworkDepositPolicyAsync(network.Id, 0m, 20m);
        var addressId = await SeedWatchedAddressAsync(await DbAsync(), network.Id, watched);
        var boundaryId = await SeedOrderAsync(network.Id, usdt, watched, 20m, addressId, userId: scUser, blockNumber: 480);
        await scan.RunTokenRoundAsync(network, usdt, gateway, 12, 50, 1440,
            TimeSpan.FromMinutes(1440), 0.9m, CancellationToken.None);
        var dbContext = await DbAsync();
        (await dbContext.DepositOrders.AsNoTracking().SingleAsync(o => o.Id == boundaryId)).SweepTaskId.ShouldNotBeNull();

        // 负值：钳为 0 = 全部持有（实体文档口径；autoMax>=0 前置条件会 fail-open 放行一切）
        await SetNetworkDepositPolicyAsync(network.Id, 0m, -5m);
        var heldId = await SeedOrderAsync(network.Id, usdt, watched, 30m, addressId, userId: scUser, blockNumber: 481);
        await scan.RunTokenRoundAsync(network, usdt, gateway, 12, 50, 1440,
            TimeSpan.FromMinutes(1440), 0.9m, CancellationToken.None);
        var held = await dbContext.DepositOrders.AsNoTracking().SingleAsync(o => o.Id == heldId);
        held.Status.ShouldBe(DepositOrderStatus.Confirmed);
        held.SweepTaskId.ShouldBeNull();
    }

    [Fact]
    public void Deposit_Fee_Pure_Functions()
    {
        // 快照：向零 8 位、费率 0 = 不收费、null 毛额（超精度路径）安全
        CollectionLedgerPoster.SnapshotDepositFee(0.001m, 20m).ShouldBe((0.001m, 0.02m, 19.98m));
        CollectionLedgerPoster.SnapshotDepositFee(0m, 20m).ShouldBe((0m, 0m, 20m));
        CollectionLedgerPoster.SnapshotDepositFee(0.003m, 10.123456789m).ShouldBe((0.003m, 0.03037037m, 10.093086419m));
        CollectionLedgerPoster.SnapshotDepositFee(0.001m, null).ShouldBe((0.001m, 0m, 0m));

        // 净额解析：快照优先；历史单（无快照无手续费）回退毛额；历史单有 fee 无快照 → 0（不入账，人工）
        AbpAdmin.Collection.Ledger.CollectionLedgerPoster.ResolveCreditAmount(20m, 19.98m, 0.02m).ShouldBe(19.98m);
        AbpAdmin.Collection.Ledger.CollectionLedgerPoster.ResolveCreditAmount(20m, null, null).ShouldBe(20m);
        AbpAdmin.Collection.Ledger.CollectionLedgerPoster.ResolveCreditAmount(20m, null, 0.02m).ShouldBe(19.98m); // 重建净额而非 0（0 会卡死归集确认 UoW）
    }

    // ── 共用 ─────────────────────────────────────────────────────────────────────────────

    private readonly List<IUnitOfWork> _openUows = new();

    private async Task RunWithAddressLockAsync(Guid addressId, Func<CollectionDbContext, Task>? beforeRelease,
        params Func<IServiceProvider, Task>[] contenders)
    {
        var running = new List<Task>();
        try
        {
            using var barrier = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var identity = addressId.ToString("D");
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtext('Collection:DepositSweepQueue'), hashtext({identity}))");

            foreach (var contender in contenders)
            {
                running.Add(RunInNewScopeAsync(contender));
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                while (await db.Database.SqlQuery<int>($"""
                    SELECT count(*)::int AS "Value" FROM pg_locks
                    WHERE locktype = 'advisory' AND NOT granted AND objsubid = 2
                      AND classid::bigint = (hashtext('Collection:DepositSweepQueue')::bigint & 4294967295)
                      AND objid::bigint = (hashtext({identity})::bigint & 4294967295)
                    """).SingleAsync(deadline.Token) < running.Count)
                {
                    await Task.Delay(25, deadline.Token);
                }
            }

            if (beforeRelease is not null)
            {
                await beforeRelease(db);
            }

            await barrier.CompleteAsync();
        }
        finally
        {
            await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    private async Task RunInNewScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = ServiceProvider.CreateScope();
        await action(scope.ServiceProvider);
    }

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
        // 不清理则本类用例间 Col/Fin 数据跨用例累计，全量断言（如链上资产合计）会 60 而非 20；
        // 相对断言（"每用例新 GUID"）察觉不到这种累计。
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
    private readonly Dictionary<string, (string To, string DataHex, long Nonce)> _prepared = new();
    private int _nextTransactionNumber;

    public Dictionary<string, List<TokenTransferLog>> Logs { get; } = new();
    public Func<string, string, long, Task>? AfterBroadcast { get; set; }
    public string? SimulatedSweepSender { get; set; }

    public long Head { get; set; }

    public int TruncateThreshold { get; set; } = int.MaxValue;

    public string? ThrowOnContract { get; set; }

    public bool HasReceipt { get; set; }
    public HashSet<string>? ReceiptHashes { get; set; }
    public HashSet<string>? ShallowReceiptHashes { get; set; }
    public HashSet<string> IncompleteReceiptLogHashes { get; } = new();
    public List<string> ReceiptLogReads { get; } = new();

    public bool ReceiptConfirmationsMet { get; set; }

    /// <summary>回执状态覆盖（null=无回执；0=revert；1=成功）。</summary>
    public int? ReceiptStatusOverride { get; set; }
    public Func<string, Task>? BeforeReceiptRead { get; set; }
    public Func<string, CancellationToken, Task>? BeforeCancellableReceiptRead { get; set; }
    public Func<Task>? BeforeSend { get; set; }
    public List<string> AddressTransferReads { get; } = new();

    /// <summary>按地址的 BNB 余额（缺省 1 = 有 gas）。</summary>
    public Dictionary<string, decimal> BnbBalances { get; } = new();

    public List<(string To, string DataHex, long Nonce, string Hash)> Broadcasts { get; } = new();

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
        if (SimulatedSweepSender is { } sender && HasReceipt && (ReceiptStatusOverride ?? 1) == 1)
        {
            foreach (var broadcast in Broadcasts)
            {
                var receiptBlock = ReceiptBlock(broadcast.Hash);
                if (broadcast.To != contractAddress || broadcast.DataHex.Length != 138
                    || (ReceiptHashes != null && !ReceiptHashes.Contains(broadcast.Hash))
                    || receiptBlock < fromBlock || receiptBlock > toBlock)
                {
                    continue;
                }

                var encodedAddress = broadcast.DataHex.Substring(10, 64).TrimStart('0');
                var address = encodedAddress == "vault" ? "0xvault" : "0x" + encodedAddress.PadLeft(40, '0');
                var amount = System.Numerics.BigInteger.Parse("0" + broadcast.DataHex.Substring(74), System.Globalization.NumberStyles.HexNumber);
                logs.Add(new TokenTransferLog(broadcast.Hash, 0, sender, address, amount.ToString(), receiptBlock));
            }
        }
        var complete = TruncateThreshold == int.MaxValue || logs.Count < TruncateThreshold;
        return Task.FromResult<(IReadOnlyList<TokenTransferLog>, bool)>((logs, complete));
    }

    public Task<AddressTransferPage> GetTransfersForAddressAsync(
        string contractAddress, string address, long fromBlock, long toBlock, int page, int pageSize,
        CancellationToken ct = default)
    {
        AddressTransferReads.Add(address);
        if (contractAddress == ThrowOnContract)
        {
            throw new ChainRpcException("simulated channel failure");
        }

        var window = Logs.TryGetValue(contractAddress, out var list)
            ? list.Where(l => l.BlockNumber >= fromBlock && l.BlockNumber <= toBlock).ToList()
            : new List<TokenTransferLog>();
        // 真分页语义（Etherscan tokentx 同款）：offset 切片 + Incomplete=还有下一页；
        // 页界重叠一条（下一页首条=上一页末条）——真实 tokentx 分页边界会出现同一笔跨页，
        // 监控的批内 (TxHash,LogIndex) 去重依赖这个形态才测得到
        if (PageSizeOverlap && page > 1 && window.Count > 0)
        {
            var start = (page - 1) * pageSize - 1;
            if (start >= window.Count)
            {
                return Task.FromResult(new AddressTransferPage(new List<TokenTransferLog>(), false));
            }
            var slice = window.Skip(start).Take(pageSize + 1).ToList();
            return Task.FromResult(new AddressTransferPage(slice, start + slice.Count < window.Count));
        }
        var hasMore = window.Count > 0 && TruncateThreshold != int.MaxValue && window.Count >= TruncateThreshold;
        if (!hasMore)
        {
            var slice = window.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult(new AddressTransferPage(slice, false));
        }
        return Task.FromResult(new AddressTransferPage(window, true));
    }

    /// <summary>地址通道按页重叠一条返回（监控批内去重用例的开关）。</summary>
    public bool PageSizeOverlap { get; set; }

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

    public async Task<TransactionReceipt> GetTransactionReceiptAsync(string txHash, CancellationToken ct = default)
    {
        if (BeforeReceiptRead != null)
        {
            await BeforeReceiptRead(txHash);
        }

        if (BeforeCancellableReceiptRead != null)
        {
            await BeforeCancellableReceiptRead(txHash, ct);
        }

        return HasReceipt && (ReceiptHashes == null || ReceiptHashes.Contains(txHash))
            ? new TransactionReceipt(txHash, ReceiptBlock(txHash), ReceiptStatusOverride ?? 1)
            : new TransactionReceipt(txHash, null, null);
    }

    private long ReceiptBlock(string txHash) =>
        ReceiptConfirmationsMet && ShallowReceiptHashes?.Contains(txHash) != true ? 400L : 495L;

    public Task<(IReadOnlyList<TokenTransferLog> Logs, bool Complete)> GetReceiptTransferLogsAsync(
        string txHash, string contractAddress, long block, CancellationToken ct = default)
    {
        ReceiptLogReads.Add(txHash);
        if (IncompleteReceiptLogHashes.Contains(txHash))
        {
            return Task.FromResult<(IReadOnlyList<TokenTransferLog>, bool)>(([], false));
        }

        return GetTransferLogsAsync(contractAddress, block, block, ct);
    }

    public Task<SignedChainTransaction> PrepareTransactionAsync(
        string privateKey, string to, string dataHex, string valueWei, long chainId, long nonce,
        CancellationToken ct = default)
    {
        var hash = "0xfake-" + ++_nextTransactionNumber;
        _prepared.Add(hash, (to, dataHex, nonce));
        return Task.FromResult(new SignedChainTransaction(hash, RawHex: hash));
    }

    public async Task BroadcastTransactionAsync(SignedChainTransaction transaction, CancellationToken ct = default)
    {
        if (BeforeSend is not null)
        {
            await BeforeSend();
        }

        var (to, dataHex, nonce) = _prepared[transaction.Hash];
        BroadcastCount++;
        Broadcasts.Add((to, dataHex, nonce, transaction.Hash));
        if (AfterBroadcast != null)
        {
            await AfterBroadcast(to, dataHex, nonce);
        }
    }

    public async Task<string> SendTransactionAsync(
        string privateKey, string to, string dataHex, string valueWei, long chainId, long nonce,
        CancellationToken ct = default)
    {
        var signed = await PrepareTransactionAsync(privateKey, to, dataHex, valueWei, chainId, nonce, ct);
        await BroadcastTransactionAsync(signed, ct);
        return signed.Hash;
    }

    public Task<decimal> GetGasPriceGweiAsync(CancellationToken ct = default) => Task.FromResult(1m);

    /// <summary>按哈希的交易 nonce（提现认领校验用；缺省 null = 节点不认识该交易）。</summary>
    public System.Collections.Generic.Dictionary<string, long> TxNonces { get; } = new();
    public Dictionary<string, string> TxSenders { get; } = new();

    public Task<long?> GetTransactionNonceAsync(string txHash, CancellationToken ct = default)
        => Task.FromResult(TxNonces.TryGetValue(txHash, out var n) ? n : (long?)null);

    public Task<long?> GetTransactionNonceForSenderAsync(string txHash, string sender, CancellationToken ct = default)
        => TxSenders.TryGetValue(txHash, out var actual) && string.Equals(actual, sender, StringComparison.OrdinalIgnoreCase)
            ? GetTransactionNonceAsync(txHash, ct)
            : Task.FromResult<long?>(null);
}
