using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

public class SweepGasHardeningTests : CollectionTestBase
{
    private const string Mnemonic = "test test test test test test test test test test test test";
    private const string MasterKey = "test-master-key";

    private SweepManager NewSweepManager() => GetRequiredService<SweepManager>().WithMasterKey(MasterKey);

    private async Task<(ColNetwork Network, ColToken Token, string GasAddress)> SeedNetworkAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var encryptor = GetRequiredService<IStringEncryptionService>();
        var cipher = HdKeyService.Protect(Mnemonic, MasterKey, encryptor);
        var gasAddress = HdKeyService.DeriveAddress(Mnemonic, HdKeyService.GasAddressIndex).ToLowerInvariant();
        var network = new ColNetwork(Guid.NewGuid())
        {
            Name = "BSC-HARDEN-" + Guid.NewGuid().ToString("N")[..8],
            ChainId = 56,
            NodeLogResultCap = 100,
            AutoCreditMaxAmount = 5000m,
            RpcCipher = "x",
            MnemonicCipher = cipher,
            CreationTime = DateTime.UtcNow,
        };
        var token = new ColToken(Guid.NewGuid())
        {
            NetworkId = network.Id,
            Symbol = "USDT",
            ContractAddress = "0x55d398326f99059ff775485246999027b3197955",
            Decimals = 18,
            MinDeposit = "10",
            IsEnabled = true,
            LastScannedBlock = 100,
            VaultAddress = "0x" + new string('v', 40),
            CreationTime = DateTime.UtcNow,
        };
        await db.Networks.AddAsync(network);
        await db.Tokens.AddAsync(token);
        await db.SaveChangesAsync();
        await uow.CompleteAsync();
        return (network, token, gasAddress);
    }

    private async Task<(Guid TaskId, Guid OrderId, Guid AddressId, string Watched)> SeedQueuedTaskAsync(
        ColNetwork network, ColToken token, decimal amount, int addressIndex = 2, long depositBlock = 120)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var userId = Guid.NewGuid();
        var addressId = Guid.NewGuid();
        var watched = ("0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8]).ToLowerInvariant();
        var taskId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        await db.DepositAddresses.AddAsync(new ColDepositAddress(addressId)
        {
            UserId = userId,
            NetworkId = network.Id,
            Address = watched,
            AddressIndex = addressIndex,
            CreationTime = DateTime.UtcNow,
        });
        await db.SweepTasks.AddAsync(new ColSweepTask(taskId)
        {
            NetworkId = network.Id,
            TokenId = token.Id,
            UserId = userId,
            AddressId = addressId,
            Address = watched,
            AddressIndex = addressIndex,
            Amount = amount,
            Status = SweepTaskStatus.Queued,
            StatusChangedAt = DateTime.UtcNow,
            CreationTime = DateTime.UtcNow,
        });
        await db.DepositOrders.AddAsync(new ColDepositOrder(orderId)
        {
            NetworkId = network.Id,
            TokenId = token.Id,
            UserId = userId,
            AddressId = addressId,
            ToAddress = watched,
            FromAddress = "0x" + new string('9', 40),
            TxHash = "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            LogIndex = 0,
            RawAmount = "20000000000000000000",
            Amount = amount,
            Fee = 0m,
            ActualAmount = amount,
            BlockNumber = depositBlock,
            Status = DepositOrderStatus.Confirmed,
            SweepTaskId = taskId,
            CreationTime = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        await uow.CompleteAsync();
        return (taskId, orderId, addressId, watched);
    }

    private Task RunRoundAsync(ColNetwork network, ColToken token, IBscChainGateway gateway, decimal fuelLimit = 0.005m)
        => NewSweepManager().RunTokenRoundAsync(
            network, token, gateway, 12, null, fuelLimit,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m,
            CancellationToken.None);

    [Fact]
    public async Task Gas_Sponsor_Serializes_Concurrent_Fueling_Tasks_Per_Network()
    {
        var (network, token, gasAddress) = await SeedNetworkAsync();
        var (t1, _, _, w1) = await SeedQueuedTaskAsync(network, token, 20m, 2);
        var (t2, _, _, w2) = await SeedQueuedTaskAsync(network, token, 30m, 3);

        var gateway = new FakeChainGateway(head: 500) { GasPriceGwei = 1m, TxCountSeed = 10, LatestTxCountSeed = 10 };
        gateway.SeedTokenBalance(token, w1, 20m);
        gateway.SeedTokenBalance(token, w2, 30m);
        gateway.BnbBalances[gasAddress] = 1m;
        gateway.BnbBalances[w1] = 0m;
        gateway.BnbBalances[w2] = 0m;

        await RunRoundAsync(network, token, gateway);

        gateway.BroadcastCount.ShouldBe(1, "同网络同一时刻只能有一笔在途打油");
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var rows = await db.SweepTasks.AsNoTracking().Where(t => t.NetworkId == network.Id).ToListAsync();
        rows.Count(t => t.GasTxHash != null).ShouldBe(1);
        var activeFuel = rows.Single(t => t.GasTxHash != null);
        activeFuel.GasNonceUsed.ShouldBe(10L);

        // 确认第一笔打油并给 w1 BNB 到位后，下一轮才会释放第二笔打油
        var firstFuelHash = activeFuel.GasTxHash!;
        gateway.HasReceipt = true;
        gateway.ReceiptHashes = [firstFuelHash];
        gateway.ReceiptConfirmationsMet = true;
        gateway.BnbBalances[activeFuel.Address] = 0.002m;
        gateway.LatestTxCountSeed = 11;
        gateway.TxCountSeed = 11;

        await RunRoundAsync(network, token, gateway);

        var rowsAfter = await db.SweepTasks.AsNoTracking().Where(t => t.NetworkId == network.Id).ToListAsync();
        var otherId = activeFuel.Id == t1 ? t2 : t1;
        var secondFuel = rowsAfter.Single(t => t.Id == otherId);
        secondFuel.GasTxHash.ShouldNotBeNull();
        secondFuel.GasNonceUsed.ShouldBe(11L);
    }

    [Fact]
    public async Task Stuck_Owned_Fuel_Head_After_Three_Minutes_Replaces_Same_Nonce_With_Bumped_Gas()
    {
        var (network, token, gasAddress) = await SeedNetworkAsync();
        var (taskId, _, _, watched) = await SeedQueuedTaskAsync(network, token, 20m, 2);

        var gateway = new FakeChainGateway(head: 500) { GasPriceGwei = 1m, TxCountSeed = 5, LatestTxCountSeed = 5 };
        gateway.SeedTokenBalance(token, watched, 20m);
        gateway.BnbBalances[gasAddress] = 1m;
        gateway.BnbBalances[watched] = 0m;

        await RunRoundAsync(network, token, gateway);
        var firstHash = gateway.Broadcasts.Single().Hash;
        var firstGasWei = gateway.BroadcastGasPrices[firstHash];

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.SweepTasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.GasBroadcastAt, DateTime.UtcNow.AddMinutes(-4)));
            await uow.CompleteAsync();
        }

        await RunRoundAsync(network, token, gateway);

        gateway.BroadcastCount.ShouldBe(2);
        var replaceCall = gateway.Broadcasts[1];
        replaceCall.Nonce.ShouldBe(5L);
        replaceCall.Hash.ShouldNotBe(firstHash);
        var replaceGasWei = gateway.BroadcastGasPrices[replaceCall.Hash];
        replaceGasWei.ShouldBeGreaterThanOrEqualTo(firstGasWei * 110 / 100 + 1);

        using var check = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var checkDb = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var task = await checkDb.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.GasTxHash.ShouldBe(replaceCall.Hash);
        task.FuelBumpSteps.ShouldBe(1);

        var attempts = await checkDb.ChainTxAttempts.AsNoTracking()
            .Where(a => a.RefId == taskId && a.Purpose == ChainTxPurpose.SweepFuel)
            .OrderBy(a => a.AttemptNo)
            .ToListAsync();
        attempts.Count.ShouldBe(2);
        attempts[0].Status.ShouldBe(ChainTxAttemptStatus.Replaced);
        attempts[0].RawSignedTx.ShouldBeNull();
        attempts[1].Status.ShouldBe(ChainTxAttemptStatus.Broadcasted);
    }

    [Fact]
    public async Task Fuel_Exceeding_Limit_Defers_Without_Failing_And_Recovers_When_Gas_Drops()
    {
        var (network, token, gasAddress) = await SeedNetworkAsync();
        var (taskId, _, _, watched) = await SeedQueuedTaskAsync(network, token, 20m, 2);

        // 20 Gwei -> fuelBudget ≈ 20 * 5.96 Gwei * 100k * 1.5 ≈ 0.01788 BNB > 0.005 limit
        var gateway = new FakeChainGateway(head: 500) { GasPriceGwei = 20m };
        gateway.SeedTokenBalance(token, watched, 20m);
        gateway.BnbBalances[gasAddress] = 1m;
        gateway.BnbBalances[watched] = 0m;

        await RunRoundAsync(network, token, gateway, fuelLimit: 0.005m);

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var task = await db.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            task.Status.ShouldBe(SweepTaskStatus.Fueling, "打油超限应保留在 Fueling 等待 gas 回落，不得永久 Failed");
            task.GasTxHash.ShouldBeNull();
        }
        gateway.BroadcastCount.ShouldBe(0);

        gateway.GasPriceGwei = 1m;
        await RunRoundAsync(network, token, gateway, fuelLimit: 0.005m);
        gateway.BroadcastCount.ShouldBe(1);
    }

    [Fact]
    public async Task Sweep_Rebroadcast_Bumps_Gas_And_ReFuels_Instead_Of_Silent_Cap_When_Bnb_Short()
    {
        var (network, token, gasAddress) = await SeedNetworkAsync();
        var (taskId, _, _, watched) = await SeedQueuedTaskAsync(network, token, 20m, 2);

        var gateway = new FakeChainGateway(head: 500) { GasPriceGwei = 1m, TxCountSeed = 7, LatestTxCountSeed = 7 };
        gateway.SeedTokenBalance(token, watched, 20m);
        gateway.BnbBalances[gasAddress] = 1m;
        // 够首笔 1 Gwei * 100k = 0.0001 BNB，但不够 1 档 1.25 Gwei * 100k = 0.000125 BNB
        gateway.BnbBalances[watched] = 0.000105m;

        await RunRoundAsync(network, token, gateway);
        gateway.BroadcastCount.ShouldBe(1);
        var firstSweepHash = gateway.Broadcasts.Single().Hash;
        gateway.BroadcastGasPrices[firstSweepHash].ShouldBe(BscGasPricePolicy.GweiToWei(1m));

        // 超过重签宽限期：期望抬价 1 档但余额不足以支付，必须退回 Fueling 补油而非静默压价重签
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.SweepTasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BroadcastAt, DateTime.UtcNow.AddMinutes(-40)));
            await uow.CompleteAsync();
        }

        await RunRoundAsync(network, token, gateway);

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var task = await db.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            task.Status.ShouldBe(SweepTaskStatus.Fueling);
            task.GasBumpSteps.ShouldBe(0, "退回 Fueling 时只保留已签发档位，避免回跳加档与后续加档叠加");
            task.TxHash.ShouldBe(firstSweepHash);
            task.NonceUsed.ShouldBe(7L);
            task.GasTxHash.ShouldNotBeNull("退回 Fueling 后同轮打油阶段立即发出补油交易");
        }
        gateway.BroadcastCount.ShouldBe(2);

        // 打油确认、地址 BNB 补足后恢复 Sweeping 并以 bumped gas 重签同 nonce
        var fuelHash = gateway.Broadcasts[1].Hash;
        gateway.HasReceipt = true;
        gateway.ReceiptHashes = [fuelHash];
        gateway.ReceiptConfirmationsMet = true;
        gateway.BnbBalances[watched] = 0.001m;

        await RunRoundAsync(network, token, gateway);
        gateway.BroadcastCount.ShouldBe(3);
        var bumpedSweep = gateway.Broadcasts[2];
        bumpedSweep.Nonce.ShouldBe(7L);
        bumpedSweep.Hash.ShouldNotBe(firstSweepHash);
        gateway.BroadcastGasPrices[bumpedSweep.Hash]
            .ShouldBeGreaterThanOrEqualTo(gateway.BroadcastGasPrices[firstSweepHash] * 110 / 100 + 1);
        using (var uow2 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db2 = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var sweptTask = await db2.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            sweptTask.GasBumpSteps.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Preflight_EthCall_Revert_Blocks_Fueling_And_Fails_Task()
    {
        var (network, token, gasAddress) = await SeedNetworkAsync();
        var (taskId, _, _, watched) = await SeedQueuedTaskAsync(network, token, 20m, 2);

        var gateway = new FakeChainGateway(head: 500)
        {
            OnSimulateCall = (_, _, _) => ChainCallSimulationResult.Revert("ERC20: paused"),
        };
        gateway.SeedTokenBalance(token, watched, 20m);
        gateway.BnbBalances[gasAddress] = 1m;
        gateway.BnbBalances[watched] = 0m;

        await RunRoundAsync(network, token, gateway);

        gateway.BroadcastCount.ShouldBe(0);
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var task = await db.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(SweepTaskStatus.Failed);
        task.Retryable.ShouldBeFalse();
        task.FailReason!.ShouldContain("eth_call");
    }

    [Fact]
    public async Task Preflight_Zero_Token_Balance_With_Zero_Nonce_Defers_Then_Fails_Without_Fueling()
    {
        var (network, token, gasAddress) = await SeedNetworkAsync();
        var (taskId, _, _, watched) = await SeedQueuedTaskAsync(network, token, 20m, 2);

        var gateway = new FakeChainGateway(head: 500) { TxCountSeed = 0, LatestTxCountSeed = 0 };
        gateway.BnbBalances[gasAddress] = 1m;
        gateway.BnbBalances[watched] = 0m;

        // 第 1、2 轮保留在 Fueling 等待节点同步且禁止打油；连续第 3 轮仍不足才转永久失败
        await RunRoundAsync(network, token, gateway);
        await RunRoundAsync(network, token, gateway);
        gateway.BroadcastCount.ShouldBe(0);
        using (var midUow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var midDb = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var mid = await midDb.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            mid.Status.ShouldBe(SweepTaskStatus.Fueling);
            mid.PreflightDeficitCount.ShouldBe(2);
        }

        await RunRoundAsync(network, token, gateway);

        gateway.BroadcastCount.ShouldBe(0);
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var task = await db.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(SweepTaskStatus.Failed);
        task.Retryable.ShouldBeFalse();
        task.PreflightDeficitCount.ShouldBe(3);
        task.FailReason!.ShouldContain("已拦截打油");
    }

    [Fact]
    public async Task Preflight_Low_Token_Balance_With_Matching_Chain_Transfer_Discovers_And_Credits_Without_Fueling()
    {
        var (network, token, gasAddress) = await SeedNetworkAsync();
        var (taskId, orderId, _, watched) = await SeedQueuedTaskAsync(network, token, 20m, 2, depositBlock: 120);

        var externalSweepHash = "0x" + new string('e', 64);
        var gateway = new FakeChainGateway(head: 500)
        {
            LatestTxCountSeed = 1, // > 0 known hashes -> trigger discover
            HasReceipt = true,
            ReceiptHashes = [externalSweepHash],
            ReceiptConfirmationsMet = true,
        };
        gateway.BnbBalances[gasAddress] = 1m;
        gateway.BnbBalances[watched] = 0m;
        gateway.Logs[token.ContractAddress] =
        [
            new TokenTransferLog(externalSweepHash, 0, watched, token.VaultAddress!, "20000000000000000000", 400),
        ];

        await RunRoundAsync(network, token, gateway);

        gateway.BroadcastCount.ShouldBe(0, "链上已有匹配归集，绝不再打油或重复归集");
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var task = await db.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        task.Status.ShouldBe(SweepTaskStatus.Swept);
        task.TxHash.ShouldBe(externalSweepHash);
        var order = await db.DepositOrders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        order.Status.ShouldBe(DepositOrderStatus.Swept);
    }

    [Fact]
    public async Task Sponsor_Head_Watchdog_Sends_Self_Transfer_When_Unowned_Head_Stuck()
    {
        var (network, _, gasAddress) = await SeedNetworkAsync();
        var gateway = new FakeChainGateway(head: 500)
        {
            GasPriceGwei = 1m,
            LatestTxCountSeed = 22,
            TxCountSeed = 23, // pending > latest 且无任何 Fueling 任务认领 nonce=22
        };

        var manager = NewSweepManager();
        await manager.RunSponsorHeadWatchdogAsync(network, gateway, CancellationToken.None);
        gateway.BroadcastCount.ShouldBe(0, "首次观测不立刻自转");

        // 使用可控时钟验证：首次观测不动，6 分钟后实际发送 0-BNB 自转并记录 ColChainTxAttempt
        var fakeClock = new OffsetClock(DateTime.UtcNow);
        var managerWithFakeClock = new SweepManager(
            GetRequiredService<IDbContextProvider<CollectionDbContext>>(),
            GetRequiredService<Balances.CollectionBalanceManager>(),
            GetRequiredService<Ledger.CollectionLedgerPoster>(),
            GetRequiredService<ChainAlertService>(),
            GetRequiredService<IStringEncryptionService>(),
            fakeClock,
            GetRequiredService<IUnitOfWorkManager>())
        {
            LazyServiceProvider = GetRequiredService<Volo.Abp.DependencyInjection.IAbpLazyServiceProvider>(),
        }.WithMasterKey(MasterKey);

        await managerWithFakeClock.RunSponsorHeadWatchdogAsync(network, gateway, CancellationToken.None);
        gateway.BroadcastCount.ShouldBe(0);

        fakeClock.UtcNow = fakeClock.UtcNow.AddMinutes(6);
        await managerWithFakeClock.RunSponsorHeadWatchdogAsync(network, gateway, CancellationToken.None);

        gateway.BroadcastCount.ShouldBe(1);
        var tx = gateway.Broadcasts.Single();
        tx.To.ToLowerInvariant().ShouldBe(gasAddress);
        tx.DataHex.ShouldBe(string.Empty);
        tx.Nonce.ShouldBe(22L);

        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var attempt = await db.ChainTxAttempts.AsNoTracking()
            .SingleAsync(a => a.NetworkId == network.Id && a.Purpose == ChainTxPurpose.SponsorHeadSelfTransfer);
        attempt.Nonce.ShouldBe(22L);
        attempt.Status.ShouldBe(ChainTxAttemptStatus.Broadcasted);
    }

    private sealed class OffsetClock(DateTime initialUtc) : Volo.Abp.Timing.IClock
    {
        public DateTime UtcNow { get; set; } = initialUtc;
        public DateTime Now => UtcNow;
        public DateTimeKind Kind => DateTimeKind.Utc;
        public bool SupportsMultipleTimezone => true;
        public DateTime Normalize(DateTime dateTime) => dateTime;
        public DateTime ConvertToUserTime(DateTime dateTime) => dateTime;
        public DateTimeOffset ConvertToUserTime(DateTimeOffset dateTimeOffset) => dateTimeOffset;
        public DateTime ConvertToUtc(DateTime dateTime) => dateTime.Kind == DateTimeKind.Utc ? dateTime : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
    }
}
