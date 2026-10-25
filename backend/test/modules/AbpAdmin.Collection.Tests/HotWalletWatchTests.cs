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

        (await ReadStatesAsync())[0].LastError.ShouldBeNull();
        (await ReadStatesAsync())[0].LastScannedBlock.ShouldBe(505);

        // 重扫幂等：游标推进后再把区间覆盖一遍（头再涨），既有哈希不重插
        gateway.Head = 540;
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);
        (await ReadTxnsAsync()).Count.ShouldBe(3);
    }

    [Fact]
    public async Task Incomplete_Pages_Keep_Cursor_And_Retry_Succeeds()
    {
        var (network, usdt) = await SeedChainWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500);
        var manager = Manager();
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None); // 基线 485

        gateway.Head = 520;
        gateway.TruncateThreshold = 1; // 每页都报未读完 → 分页超限
        gateway.Logs[usdt.ContractAddress] = new()
        {
            Log("0xaaa", Wallet, User1, "1000000000000000000", 490),
        };

        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        var state = (await ReadStatesAsync())[0];
        state.LastScannedBlock.ShouldBe(485); // 游标未推进
        state.LastError.ShouldNotBeNull();
        (await CountTxnsAsync()).ShouldBe(0); // 不完整区间不落任何行

        // 下一轮读全（阈值放开 + 头再涨）：同区间重扫落行
        gateway.TruncateThreshold = int.MaxValue;
        gateway.Head = 540;
        await manager.RunRoundAsync(network, gateway, 15, CancellationToken.None);
        (await CountTxnsAsync()).ShouldBe(1);
        (await ReadStatesAsync())[0].LastError.ShouldBeNull();
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
        acked.AckedBy.ShouldNotBeNull();
        acked.AckNote.ShouldBe("已核实：运营手动转出");

        // 状态门：非 Anomaly 拒绝、不存在拒绝
        (await Should.ThrowAsync<BusinessException>(() => admin.AckHotWalletTxnAsync(anomalyId)))
            .Code.ShouldContain("HotWalletTxnNotAckable");
        (await Should.ThrowAsync<BusinessException>(() => admin.AckHotWalletTxnAsync(inId)))
            .Code.ShouldContain("HotWalletTxnNotAckable");
        (await Should.ThrowAsync<BusinessException>(() => admin.AckHotWalletTxnAsync(Guid.NewGuid())))
            .Code.ShouldContain("HotWalletTxnNotFound");
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
