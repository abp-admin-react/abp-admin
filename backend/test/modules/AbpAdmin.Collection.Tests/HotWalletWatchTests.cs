using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 热钱包逐笔监控锁：三分支分类（出站匹配提现单 Matched / 出站未匹配 Anomaly / 入站 ExternalIn）、
/// 首跑基线不回扫、重扫幂等（(TxHash,LogIndex) 唯一）、分页读不全游标不推进、
/// 未配置热钱包整轮跳过、ack 端点 CAS + 状态门。
/// </summary>
public class HotWalletWatchTests : CollectionTestBase
{
    private const string Wallet = "0xhot00000000000000000000000000000000000f1";
    private const string User1 = "0xuser000000000000000000000000000000000001";

    [Fact]
    public async Task First_Run_Writes_Baseline_Without_Backscan()
    {
        var (network, usdt) = await SeedChainWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500);
        gateway.Logs[usdt.ContractAddress] = new()
        {
            Log("0xold", Wallet, User1, "1000000000000000000", 100), // 基线之前的旧流水
        };

        await Manager().RunRoundAsync(network, gateway, 15, CancellationToken.None);

        var states = await ReadStatesAsync();
        states.Count.ShouldBe(1);
        states[0].LastScannedBlock.ShouldBe(500 - 15); // 基线 = 安全链头，不回扫
        (await CountTxnsAsync()).ShouldBe(0); // 旧流水不入表
    }

    [Fact]
    public async Task Classifies_Matched_Anomaly_And_ExternalIn_Then_Idempotent_On_Rescan()
    {
        var (network, usdt) = await SeedChainWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500);
        var manager = Manager();
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None); // 基线 485

        var withdrawId = await InsertWithdrawWithHashAsync("0xaaa", network.Name);
        gateway.Head = 520; // safeHead 505
        gateway.Logs[usdt.ContractAddress] = new()
        {
            Log("0xaaa", Wallet, User1, "9960000000000000000", 490),   // 出站 + 匹配提现单
            Log("0xbbb", Wallet, User1, "5000000000000000000", 491),   // 出站 + 未匹配 → 异常
            Log("0xccc", User1, Wallet, "20000000000000000000", 492),  // 入站 → 外部补币
        };

        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        var txns = await ReadTxnsAsync();
        txns.Count.ShouldBe(3);
        var matched = txns.Single(t => t.TxHash == "0xaaa");
        matched.Status.ShouldBe(HotWalletTxnStatus.Matched);
        matched.Direction.ShouldBe(HotWalletDirection.Out);
        matched.MatchedWithdrawId.ShouldBe(withdrawId);
        matched.Amount.ShouldBe(9.96m);

        var anomaly = txns.Single(t => t.TxHash == "0xbbb");
        anomaly.Status.ShouldBe(HotWalletTxnStatus.Anomaly);
        anomaly.MatchedWithdrawId.ShouldBeNull();

        var externalIn = txns.Single(t => t.TxHash == "0xccc");
        externalIn.Status.ShouldBe(HotWalletTxnStatus.ExternalIn);
        externalIn.Direction.ShouldBe(HotWalletDirection.In);

        // 安全载荷锁：每笔未匹配出账恰推一条 ChainHotWalletChanged（匹配/入站不推）
        var pushed = Fakes.FakeChainPushNotifier.Instance.HotWallet
            .Where(e => e.TxHash == "0xbbb").ToList();
        pushed.Count.ShouldBe(1);
        pushed[0].Symbol.ShouldBe("USDT");
        pushed[0].Direction.ShouldBe("Out");
        Fakes.FakeChainPushNotifier.Instance.HotWallet.Count.ShouldBe(1);

        (await ReadStatesAsync())[0].LastError.ShouldBeNull();
        (await ReadStatesAsync())[0].LastScannedBlock.ShouldBe(505);

        // 重扫幂等：游标推进后再把区间覆盖一遍（头再涨），既有哈希不重插
        gateway.Head = 540;
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);
        (await ReadTxnsAsync()).Count.ShouldBe(3);
        Fakes.FakeChainPushNotifier.Instance.HotWallet.Count.ShouldBe(1); // 重扫不重推
    }

    [Fact]
    public async Task Overrun_Halves_Window_And_Converges()
    {
        var (network, usdt) = await SeedChainWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500);
        var manager = Manager();
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None); // 基线 485

        // 两笔流水在不同块：阈值 2 → 整窗读不全 → 缩半分窗后每窗 1 笔可读全 → 自愈推进
        gateway.Head = 520;
        gateway.TruncateThreshold = 2;
        gateway.Logs[usdt.ContractAddress] = new()
        {
            Log("0xaaa", Wallet, User1, "1000000000000000000", 490),
            Log("0xbbb", Wallet, User1, "1000000000000000000", 494),
        };

        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        var txns = await ReadTxnsAsync();
        txns.Count.ShouldBe(2); // 缩半自愈：两笔全部入库（未匹配出账 → 均为异常）
        txns.All(t => t.Status == HotWalletTxnStatus.Anomaly).ShouldBeTrue();
        (await ReadStatesAsync())[0].LastScannedBlock.ShouldBe(505); // 游标推到头
        (await ReadStatesAsync())[0].LastError.ShouldBeNull(); // 自愈不留错
    }

    [Fact]
    public async Task Single_Block_Overrun_Alerts_Skips_Block_And_Advances()
    {
        var (network, usdt) = await SeedChainWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500);
        var manager = Manager();
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None); // 基线 485

        // 同块两笔 + 阈值 2：缩到单块仍读不全 → 告警跳块推进（监控卡死比漏一段更危险）
        gateway.Head = 520;
        gateway.TruncateThreshold = 2;
        gateway.Logs[usdt.ContractAddress] = new()
        {
            Log("0xaaa", Wallet, User1, "1000000000000000000", 490),
            Log("0xbbb", Wallet, User1, "1000000000000000000", 490),
            Log("0xccc", Wallet, User1, "1000000000000000000", 495), // 块 495 单笔可读全
        };

        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        var txns = await ReadTxnsAsync();
        txns.Count.ShouldBe(1);
        txns.Single().TxHash.ShouldBe("0xccc"); // 跳过的块不落行，后续块照常
        var state = (await ReadStatesAsync())[0];
        state.LastScannedBlock.ShouldBe(505); // 不卡死：游标照常推到头
        state.LastError.ShouldNotBeNull(); // 跳块留痕
        state.LastError.ShouldContain("490");
    }

    [Fact]
    public async Task One_Token_Failure_Does_Not_Block_The_Other()
    {
        var (network, usdt) = await SeedChainWithWalletAsync();
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await DbAsync();
            await dbContext.Tokens.AddAsync(new ColToken(Guid.NewGuid())
            {
                NetworkId = network.Id,
                Symbol = "USDC",
                ContractAddress = "0xusdc",
                Decimals = 18,
                MinDeposit = "10",
                IsEnabled = true,
                CreationTime = DateTime.UtcNow,
            });
            await dbContext.SaveChangesAsync();
            await uow.CompleteAsync();
        }

        var gateway = new FakeChainGateway(head: 500);
        var manager = Manager();
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None); // 双币基线

        gateway.Head = 520;
        gateway.ThrowOnContract = "0xusdc"; // 第二币通道故障
        gateway.Logs[usdt.ContractAddress] = new()
        {
            Log("0xaaa", Wallet, User1, "1000000000000000000", 490),
        };

        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        (await ReadTxnsAsync()).Count.ShouldBe(1); // USDT 正常落行
        var states = await ReadStatesAsync();
        states.Count.ShouldBe(2); // 双币游标行都在
        states.Single(s => s.Symbol == "USDT").LastScannedBlock.ShouldBe(505);
        // USDC 抛错被逐币 catch：游标留待下轮（本轮安全链头未推），不挡 USDT
        states.Single(s => s.Symbol == "USDC").LastScannedBlock.ShouldBe(485);
    }

    [Fact]
    public async Task No_Wallet_Configured_Skips_Round_Entirely()
    {
        var (network, usdt) = await SeedChainAsync(withWallet: false);
        var gateway = new FakeChainGateway(head: 500);
        gateway.Logs[usdt.ContractAddress] = new()
        {
            Log("0xaaa", Wallet, User1, "1000000000000000000", 490),
        };

        await Manager().RunRoundAsync(network, gateway, 15, CancellationToken.None);

        (await ReadStatesAsync()).Count.ShouldBe(0); // 未配置：连游标行都不建
        (await CountTxnsAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Ack_CAS_And_Status_Gate()
    {
        var (network, usdt) = await SeedChainWithWalletAsync();
        var anomalyId = await InsertTxnAsync(network.Id, usdt.Id, "0xbbb", HotWalletTxnStatus.Anomaly);
        var inId = await InsertTxnAsync(network.Id, usdt.Id, "0xccc", HotWalletTxnStatus.ExternalIn);

        var admin = GetRequiredService<CollectionWithdrawAdminAppService>();
        await admin.AckHotWalletTxnAsync(anomalyId, "已核实：运营手动转出");

        Entities.ColHotWalletTxn? acked = null;
        await WithUnitOfWorkAsync(async () =>
        {
            acked = await (await DbAsync()).HotWalletTxns.AsNoTracking()
                .SingleAsync(t => t.Id == anomalyId);
        });
        acked!.Status.ShouldBe(HotWalletTxnStatus.Acked);
        acked.AckedBy.ShouldNotBeNullOrWhiteSpace(); // 测试上下文为已登录 admin；生产行为=当前用户名
        acked.AckedAt.ShouldNotBeNull();
        acked.AckNote.ShouldBe("已核实：运营手动转出");

        // 状态门：非 Anomaly 拒绝、不存在拒绝
        (await Should.ThrowAsync<BusinessException>(() => admin.AckHotWalletTxnAsync(anomalyId)))
            .Code.ShouldContain("HotWalletTxnNotAckable");
        (await Should.ThrowAsync<BusinessException>(() => admin.AckHotWalletTxnAsync(inId)))
            .Code.ShouldContain("HotWalletTxnNotAckable");
        (await Should.ThrowAsync<BusinessException>(() => admin.AckHotWalletTxnAsync(Guid.NewGuid())))
            .Code.ShouldContain("HotWalletTxnNotFound");
    }

    [Fact]
    public async Task Page_Overlap_Duplicates_Are_Deduped_In_Batch()
    {
        // Etherscan 页界重叠：同一笔出现在两页 → 批内去重必须吸收（否则唯一键炸整个事务）
        var (network, usdt) = await SeedChainWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500) { PageSizeOverlap = true };
        var manager = Manager();
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        gateway.Head = 520;
        // 两条不同哈希 + fake 重叠语义：页2 首条重复页1 末条
        gateway.Logs[usdt.ContractAddress] = new()
        {
            Log("0xaaa", Wallet, User1, "1000000000000000000", 490),
            Log("0xbbb", Wallet, User1, "1000000000000000000", 491),
        };

        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        var txns = await ReadTxnsAsync();
        txns.Count.ShouldBe(2); // 重叠页不产生重复行
    }

    [Fact]
    public async Task Skip_Error_Is_Cleared_By_Next_Clean_Round()
    {
        // 跳块留痕 → 干净轮清错（状态卡不能永久报错）
        var (network, usdt) = await SeedChainWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500);
        var manager = Manager();
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        gateway.Head = 520;
        gateway.TruncateThreshold = 2;
        gateway.Logs[usdt.ContractAddress] = new()
        {
            Log("0xaaa", Wallet, User1, "1000000000000000000", 490),
            Log("0xbbb", Wallet, User1, "1000000000000000000", 490),
        };
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);
        (await ReadStatesAsync())[0].LastError.ShouldNotBeNull();

        gateway.TruncateThreshold = int.MaxValue;
        gateway.Logs[usdt.ContractAddress] = new(); // 干净轮：无新流水
        gateway.Head = 540;
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);
        (await ReadStatesAsync())[0].LastError.ShouldBeNull();
    }

    [Fact]
    public async Task Ack_Note_Length_Is_Enforced_Server_Side()
    {
        var (network, usdt) = await SeedChainWithWalletAsync();
        var anomalyId = await InsertTxnAsync(network.Id, usdt.Id, "0xerr", HotWalletTxnStatus.Anomaly);

        var admin = GetRequiredService<CollectionWithdrawAdminAppService>();
        var ex = await Should.ThrowAsync<BusinessException>(() =>
            admin.AckHotWalletTxnAsync(anomalyId, new string('长', 600)));
        ex.Code.ShouldContain("HotWalletTxnNotAckable");
    }

    // ── 造数 ─────────────────────────────────────────────────────────────────────────────

    private HotWalletWatchManager Manager() => GetRequiredService<HotWalletWatchManager>();

    private static TokenTransferLog Log(string hash, string from, string to, string raw, long block) =>
        new(hash, 0, from, to, raw, block);

    private async Task<(ColNetwork Network, ColToken Usdt)> SeedChainWithWalletAsync()
        => await SeedChainAsync(withWallet: true);

    private async Task<(ColNetwork Network, ColToken Usdt)> SeedChainAsync(bool withWallet)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await DbAsync();
        var network = new ColNetwork(Guid.NewGuid())
        {
            Name = "BSC-HW-" + Guid.NewGuid().ToString("N")[..8],
            ChainId = 97,
            NodeLogResultCap = 100,
            CreationTime = DateTime.UtcNow,
            WithdrawVaultAddress = withWallet ? Wallet : null,
        };
        await dbContext.Networks.AddAsync(network);
        var usdt = new ColToken(Guid.NewGuid())
        {
            NetworkId = network.Id,
            Symbol = "USDT",
            ContractAddress = "0xusdt",
            Decimals = 18,
            MinDeposit = "10",
            IsEnabled = true,
            CreationTime = DateTime.UtcNow,
        };
        await dbContext.Tokens.AddAsync(usdt);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return (network, usdt);
    }

    private async Task<Guid> InsertWithdrawWithHashAsync(string txHash, string networkName)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await DbAsync();
        var order = new ColWithdrawOrder(Guid.NewGuid())
        {
            UserId = Guid.NewGuid(),
            Currency = CollectionCurrency.Usdt,
            NetworkName = networkName,
            ToAddress = User1,
            Amount = 10m,
            Fee = 0.04m,
            ActualAmount = 9.96m,
            FeeRate = 0.004m,
            Status = WithdrawStatus.Completed,
            TxHash = txHash,
            NonceUsed = 7,
            CompleteTime = DateTime.UtcNow,
            StatusChangedAt = DateTime.UtcNow,
            CreationTime = DateTime.UtcNow,
        };
        await dbContext.WithdrawOrders.AddAsync(order);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return order.Id;
    }

    private async Task<Guid> InsertTxnAsync(
        Guid networkId, Guid tokenId, string hash, HotWalletTxnStatus status)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await DbAsync();
        var txn = new ColHotWalletTxn(Guid.NewGuid())
        {
            NetworkId = networkId,
            TokenId = tokenId,
            Symbol = "USDT",
            TxHash = hash,
            LogIndex = 0,
            From = Wallet,
            To = User1,
            Direction = status == HotWalletTxnStatus.ExternalIn
                ? HotWalletDirection.In
                : HotWalletDirection.Out,
            RawAmount = "1000000000000000000",
            Amount = 1m,
            BlockNumber = 490,
            Status = status,
            CreationTime = DateTime.UtcNow,
        };
        await dbContext.HotWalletTxns.AddAsync(txn);
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
        return txn.Id;
    }

    private async Task<System.Collections.Generic.List<ColHotWalletWatchState>> ReadStatesAsync()
    {
        System.Collections.Generic.List<ColHotWalletWatchState> states = new();
        await WithUnitOfWorkAsync(async () =>
        {
            states = await (await DbAsync()).HotWalletWatchStates.AsNoTracking().ToListAsync();
        });
        return states;
    }

    private async Task<System.Collections.Generic.List<ColHotWalletTxn>> ReadTxnsAsync()
    {
        System.Collections.Generic.List<ColHotWalletTxn> txns = new();
        await WithUnitOfWorkAsync(async () =>
        {
            txns = await (await DbAsync()).HotWalletTxns.AsNoTracking().ToListAsync();
        });
        return txns;
    }

    private async Task<int> CountTxnsAsync()
    {
        var count = 0;
        await WithUnitOfWorkAsync(async () =>
        {
            count = await (await DbAsync()).HotWalletTxns.AsNoTracking().CountAsync();
        });
        return count;
    }

    private async Task<CollectionDbContext> DbAsync()
        => await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
}
