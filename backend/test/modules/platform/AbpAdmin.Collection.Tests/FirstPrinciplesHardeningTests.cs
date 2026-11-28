using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Numerics;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Collection.Services;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Collection.Withdrawals;
using HttpAgent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Volo.Abp;
using Volo.Abp.DistributedLocking;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Security.Claims;
using Volo.Abp.Security.Encryption;
using Volo.Abp.SettingManagement;
using Volo.Abp.Settings;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

public class FirstPrinciplesHardeningTests : CollectionTestBase
{
    private const string Mnemonic = "test test test test test test test test test test test test";
    private const string MasterKey = "test-master-key";
    private const string HotWalletKey = "0x1111111111111111111111111111111111111111111111111111111111111111";
    private const string HotWallet = "0x19e7e376e7c213b7e7e7e46cc70a5dd086daff2a";
    private const string RotatedWalletKey = "0x2222222222222222222222222222222222222222222222222222222222222222";
    private static readonly string RotatedWallet = new Nethereum.Signer.EthECKey(RotatedWalletKey).GetPublicAddress().ToLowerInvariant();
    private const string Counterparty = "0x3333333333333333333333333333333333333333";
    private const string UsdtContract = "0x55d398326f99059fF775485246999027B3197955";
    private static readonly Guid User1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa3");
    private static readonly Guid AdminId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb3");

    private SweepManager NewSweepManager(VolOClockWrapper? clock = null)
    {
        if (clock == null)
        {
            return GetRequiredService<SweepManager>().WithMasterKey(MasterKey);
        }

        return new SweepManager(
            GetRequiredService<IDbContextProvider<CollectionDbContext>>(),
            GetRequiredService<CollectionBalanceManager>(),
            GetRequiredService<CollectionLedgerPoster>(),
            GetRequiredService<ChainAlertService>(),
            GetRequiredService<IStringEncryptionService>(),
            clock,
            GetRequiredService<IUnitOfWorkManager>())
        {
            LazyServiceProvider = GetRequiredService<SweepManager>().LazyServiceProvider,
        }.WithMasterKey(MasterKey);
    }

    private async Task<(ColNetwork Network, ColToken Token, string GasAddress)> SeedCustomNetworkAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var encryptor = GetRequiredService<IStringEncryptionService>();
        var cipher = HdKeyService.Protect(Mnemonic, MasterKey, encryptor);
        var gasAddress = HdKeyService.DeriveAddress(Mnemonic, HdKeyService.GasAddressIndex).ToLowerInvariant();
        var network = new ColNetwork(Guid.NewGuid())
        {
            Name = "BSC-FP-" + Guid.NewGuid().ToString("N")[..8],
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
            ContractAddress = UsdtContract.ToLowerInvariant(),
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

    private Task RunSweepRoundAsync(
        ColNetwork network, ColToken token, IBscChainGateway gateway, SweepManager? manager = null, decimal fuelLimit = 0.05m)
        => (manager ?? NewSweepManager()).RunTokenRoundAsync(
            network, token, gateway, 12, null, fuelLimit,
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m,
            CancellationToken.None);

    private async Task<(ColNetwork Network, ColToken Usdt)> ArrangeSeededWithdrawNetworkAsync()
    {
        var guard = GetRequiredService<WithdrawGuard>();
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var encryptor = GetRequiredService<IStringEncryptionService>();
        var network = await db.Networks.OrderBy(n => n.CreationTime).FirstAsync();
        network.WithdrawVaultAddress = HotWallet;
        network.WithdrawVaultKeyCipher = await guard.ProtectKeyAsync(HotWalletKey);
        network.MnemonicCipher = HdKeyService.Protect(Mnemonic, MasterKey, encryptor);
        var usdt = await db.Tokens.FirstAsync(t => t.NetworkId == network.Id && t.Symbol == "USDT");
        await db.SaveChangesAsync();
        await uow.CompleteAsync();
        return (network, usdt);
    }

    // ── T1 (P0-1): 消除 Fueling ↔ Sweeping 乒乓活锁 + 差额精确补油 ──────────────────────

    [Fact]
    public async Task T1_Sweep_Bounce_To_Fueling_When_Gas_Drops_Does_Not_PingPong_And_Tops_Up_Delta()
    {
        var (network, token, gasAddress) = await SeedCustomNetworkAsync();
        var (taskId, _, _, watched) = await SeedQueuedTaskAsync(network, token, 20m, 2);

        // 首笔在 5 Gwei 签发（地址刚好够 0.0005 BNB）
        var gateway = new FakeChainGateway(head: 500) { GasPriceGwei = 5m, TxCountSeed = 3, LatestTxCountSeed = 3 };
        gateway.SeedTokenBalance(token, watched, 20m);
        gateway.BnbBalances[gasAddress] = 1m;
        gateway.BnbBalances[watched] = 0.0005m;

        await RunSweepRoundAsync(network, token, gateway);
        gateway.BroadcastCount.ShouldBe(1);
        var firstSweepHash = gateway.Broadcasts.Single().Hash;
        var fiveGweiWei = BscGasPricePolicy.GweiToWei(5m);
        gateway.BroadcastGasPrices[firstSweepHash].ShouldBe(fiveGweiWei);

        // 链上 gas 暴跌回 1 Gwei；地址余 0.0002 BNB（够 1.25 Gwei，但不够替换门槛 5.5 Gwei + 1 wei）
        gateway.GasPriceGwei = 1m;
        gateway.BnbBalances[watched] = 0.0002m;
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.SweepTasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.BroadcastAt, DateTime.UtcNow.AddMinutes(-40)));
            await uow.CompleteAsync();
        }

        // 第 2 轮：退回 Fueling 并按统一公式算差额补油，不提前把 GasBumpSteps 改成 1
        await RunSweepRoundAsync(network, token, gateway);
        gateway.BroadcastCount.ShouldBe(2);
        var fuelHash = gateway.Broadcasts[1].Hash;
        using (var uow2 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var task = await db.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            task.Status.ShouldBe(SweepTaskStatus.Fueling);
            task.GasBumpSteps.ShouldBe(0);
            task.GasTxHash.ShouldBe(fuelHash);
        }

        // 验证打油差额：target = ReplacementGasWei(ApplyBump(1 Gwei, 1), 5 Gwei) * 100k * 1.5 - 0.0002 BNB > 0.0005 BNB
        var expectedMinReplaceWei = BscGasPricePolicy.ReplacementGasWei(
            BscGasPricePolicy.ApplyBump(BscGasPricePolicy.GweiToWei(1m), 1), fiveGweiWei);
        var expectedDeltaWei = BscGasPricePolicy.ComputeDeltaFuelWei(
            BscGasPricePolicy.GweiToWei(1m),
            expectedMinReplaceWei,
            BscGasPricePolicy.EtherToWei(0.0002m),
            BscGasPricePolicy.EtherToWei(SweepManager.MinimumFuelBnb));
        expectedDeltaWei.ShouldBeGreaterThan(BscGasPricePolicy.EtherToWei(SweepManager.MinimumFuelBnb));

        // 第 3 轮（打油已在节点 mempool 等待确认，地址仍只有 0.0002 BNB）：绝不能被 1.25 Gwei 误判晋升回 Sweeping（零乒乓！）
        gateway.TxNonces[fuelHash] = gateway.TxCountSeed;
        await RunSweepRoundAsync(network, token, gateway);
        gateway.BroadcastCount.ShouldBe(2, "打油确认前地址 BNB 仍低于 5.5 Gwei+1 wei 门槛，绝不能乒乓晋升");
        using (var uow3 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var task = await db.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            task.Status.ShouldBe(SweepTaskStatus.Fueling);
        }

        // 打油确认且 BNB 到账 → 晋升并用 >= 5.5 Gwei + 1 wei 成功签发替换归集，GasBumpSteps 递增为 1
        gateway.HasReceipt = true;
        gateway.ReceiptHashes = [fuelHash];
        gateway.ReceiptConfirmationsMet = true;
        gateway.BnbBalances[watched] = 0.0002m + BscGasPricePolicy.WeiToEther(expectedDeltaWei);

        await RunSweepRoundAsync(network, token, gateway);
        gateway.BroadcastCount.ShouldBe(3);
        var replaceSweep = gateway.Broadcasts[2];
        replaceSweep.Nonce.ShouldBe(3L);
        gateway.BroadcastGasPrices[replaceSweep.Hash].ShouldBeGreaterThanOrEqualTo(expectedMinReplaceWei);
        using var uow4 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db4 = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var doneTask = await db4.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
        doneTask.Status.ShouldBe(SweepTaskStatus.Sweeping);
        doneTask.GasBumpSteps.ShouldBe(1);
    }

    // ── T2 (P0-2): 打油替换全候选回执认领（H1 先上链不丢费账/不双打油 + H2 拒播不卡 3 分钟）────────

    [Fact]
    public async Task T2_Fuel_Slot_Resolves_Older_Candidate_And_Deterministic_Reject_Allows_Immediate_Retry()
    {
        var (network, token, gasAddress) = await SeedCustomNetworkAsync();
        var (taskId, _, _, watched) = await SeedQueuedTaskAsync(network, token, 20m, 2);

        var gateway = new FakeChainGateway(head: 500)
        {
            GasPriceGwei = 1m,
            TxCountSeed = 5,
            LatestTxCountSeed = 5,
            ReceiptGasUsed = 21_000,
            ReceiptEffectiveGasPriceWei = BscGasPricePolicy.GweiToWei(1m),
        };
        gateway.SeedTokenBalance(token, watched, 20m);
        gateway.BnbBalances[gasAddress] = 1m;
        gateway.BnbBalances[watched] = 0m;

        await RunSweepRoundAsync(network, token, gateway);
        var h1 = gateway.Broadcasts[0].Hash;

        // 拨回 4 分钟；先让 H2 被节点以 replacement underpriced 确定性拒收
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.SweepTasks.Where(t => t.Id == taskId)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.GasBroadcastAt, DateTime.UtcNow.AddMinutes(-4)));
            await uow.CompleteAsync();
        }

        gateway.BeforeSend = () => throw new ChainRpcException(
            "rejected", isKeyError: false, rpcErrorCode: -32000, rpcErrorMessage: "replacement transaction underpriced");
        await RunSweepRoundAsync(network, token, gateway);

        using (var rejCheck = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var task = await db.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId);
            task.GasTxHash.ShouldBe(h1, "H2 被拒时不得覆写 GasTxHash 与 GasBroadcastAt");
            var attempts = await db.ChainTxAttempts.AsNoTracking()
                .Where(a => a.RefId == taskId && a.Purpose == ChainTxPurpose.SweepFuel)
                .OrderBy(a => a.AttemptNo)
                .ToListAsync();
            attempts.Count.ShouldBe(2);
            attempts[1].Status.ShouldBe(ChainTxAttemptStatus.Rejected);
        }

        // 下一轮立刻重签并发出 H3（无需再等 3 分钟）
        gateway.BeforeSend = null;
        await RunSweepRoundAsync(network, token, gateway);
        gateway.BroadcastCount.ShouldBe(2);
        var h3 = gateway.Broadcasts[1].Hash;
        h3.ShouldNotBe(h1);

        // 随后旧候选 H1 实际打包确认 → 下一轮必须从全候选认领 H1 并记录 H1 的 GasFeeLog，绝不漏记或双打油
        gateway.HasReceipt = true;
        gateway.ReceiptHashes = [h1];
        gateway.ReceiptConfirmationsMet = true;
        gateway.BnbBalances[watched] = 0.002m;

        await RunSweepRoundAsync(network, token, gateway);
        using var check = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var checkDb = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var feeLog = await checkDb.GasFeeLogs.AsNoTracking()
            .SingleAsync(g => g.NetworkId == network.Id && g.Purpose == ChainTxPurpose.SweepFuel && g.RefId == taskId);
        feeLog.TxHash.ShouldBe(h1);
        feeLog.FeeBnb.ShouldBe(0.000021m);

        var attemptsFinal = await checkDb.ChainTxAttempts.AsNoTracking()
            .Where(a => a.RefId == taskId && a.Purpose == ChainTxPurpose.SweepFuel)
            .OrderBy(a => a.AttemptNo)
            .ToListAsync();
        attemptsFinal.Single(a => a.TxHash == h1).Status.ShouldBe(ChainTxAttemptStatus.Confirmed);
        attemptsFinal.Single(a => a.TxHash == h3).Status.ShouldBe(ChainTxAttemptStatus.Replaced);
    }

    // ── T3 (P1-1A): 滞后 RPC 返回 0 余额时不误杀真充值，下轮恢复 ──────────────────────

    [Fact]
    public async Task T3_Transient_Stale_Rpc_Zero_Balance_Defers_And_Recovers_Next_Round()
    {
        var (network, token, gasAddress) = await SeedCustomNetworkAsync();
        var (taskId, _, _, watched) = await SeedQueuedTaskAsync(network, token, 20m, 2);

        // 第 1 轮 RPC 视图滞后读到 0；第 2 轮节点同步后读到 20
        var gateway = new FakeChainGateway(head: 500) { TxCountSeed = 0, LatestTxCountSeed = 0 };
        gateway.BnbBalances[gasAddress] = 1m;
        gateway.BnbBalances[watched] = 0m;

        await RunSweepRoundAsync(network, token, gateway);
        gateway.BroadcastCount.ShouldBe(0);
        using (var uow1 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var t = await db.SweepTasks.AsNoTracking().SingleAsync(x => x.Id == taskId);
            t.Status.ShouldBe(SweepTaskStatus.Fueling);
            t.PreflightDeficitCount.ShouldBe(1);
        }

        gateway.SeedTokenBalance(token, watched, 20m);
        await RunSweepRoundAsync(network, token, gateway);
        gateway.BroadcastCount.ShouldBe(1, "恢复真实余额后应清零误警计数并正常打油");
        using var uow2 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db2 = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var t2 = await db2.SweepTasks.AsNoTracking().SingleAsync(x => x.Id == taskId);
        t2.PreflightDeficitCount.ShouldBe(0);
        t2.GasTxHash.ShouldNotBeNull();
    }

    // ── T4 (P1-1B): Nonce 预检不被同址替换过的 PreviousTxHashes 误算分母而跳过 Discover ──

    [Fact]
    public async Task T4_Discover_Counts_Confirmed_Nonces_Not_Unmined_PreviousTxHashes()
    {
        var (network, token, gasAddress) = await SeedCustomNetworkAsync();
        var (taskId2, orderId2, addressId, watched) = await SeedQueuedTaskAsync(network, token, 30m, 2, depositBlock: 200);

        // 先构造同一充值地址上的历史 Swept 任务（消耗 1 个链上 nonce=0，但替换过 2 次，共 3 个哈希）
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.SweepTasks.AddAsync(new ColSweepTask(Guid.NewGuid())
            {
                NetworkId = network.Id,
                TokenId = token.Id,
                UserId = Guid.NewGuid(),
                AddressId = addressId,
                Address = watched,
                AddressIndex = 2,
                Amount = 10m,
                Status = SweepTaskStatus.Swept,
                TxHash = "0x" + new string('1', 64),
                PreviousTxHashes = ["0x" + new string('2', 64), "0x" + new string('3', 64)],
                NonceUsed = 0,
                BroadcastVaultAddress = token.VaultAddress,
                StatusChangedAt = DateTime.UtcNow.AddHours(-1),
                CreationTime = DateTime.UtcNow.AddHours(-1),
            });
            await db.SaveChangesAsync();
            await uow.CompleteAsync();
        }

        // 第 2 笔充值已被外部归集（链上 latestNonce=2，已确认 nonce=1 -> 2 > 1！但旧代码会算 2 <= 3 误跳过）
        var externalHash2 = "0x" + new string('f', 64);
        var gateway = new FakeChainGateway(head: 500)
        {
            LatestTxCountSeed = 2,
            TxCountSeed = 2,
            HasReceipt = true,
            ReceiptHashes = [externalHash2],
            ReceiptConfirmationsMet = true,
        };
        gateway.BnbBalances[gasAddress] = 1m;
        gateway.BnbBalances[watched] = 0m;
        gateway.Logs[token.ContractAddress] =
        [
            new TokenTransferLog(externalHash2, 0, watched, token.VaultAddress!, "30000000000000000000", 400),
        ];

        await RunSweepRoundAsync(network, token, gateway);

        gateway.BroadcastCount.ShouldBe(0);
        using var check = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var checkDb = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var settled = await checkDb.SweepTasks.AsNoTracking().SingleAsync(t => t.Id == taskId2);
        settled.Status.ShouldBe(SweepTaskStatus.Swept);
        settled.TxHash.ShouldBe(externalHash2);
        (await checkDb.DepositOrders.AsNoTracking().SingleAsync(o => o.Id == orderId2)).Status.ShouldBe(DepositOrderStatus.Swept);
    }

    // ── T5 (P1-2A + P1-2B): 提现批内 DB 串行 Nonce + 卡池同 Nonce 自动加速 + 旧候选认领 ──

    [Fact]
    public async Task T5_Withdraw_Batch_Assigns_Serial_Nonces_And_Stuck_Head_AutoBumps_Then_Settles_Winning_Candidate()
    {
        var (network, usdt) = await ArrangeSeededWithdrawNetworkAsync();
        var balances = GetRequiredService<CollectionBalanceManager>();
        var poster = GetRequiredService<CollectionLedgerPoster>();
        var withdraws = GetRequiredService<WithdrawManager>();
        var broadcast = GetRequiredService<WithdrawBroadcastManager>();

        await balances.CreditAsync(User1, CollectionCurrency.Usdt, 1000m, $"dep-{Guid.NewGuid()}");
        await poster.PostOpeningAsync(User1, CollectionCurrency.Usdt, 1000m);

        var o1 = await withdraws.SubmitAsync(User1, CollectionCurrency.Usdt, Counterparty, 20m);
        var o2 = await withdraws.SubmitAsync(User1, CollectionCurrency.Usdt, Counterparty, 21m);
        var o3 = await withdraws.SubmitAsync(User1, CollectionCurrency.Usdt, Counterparty, 22m);
        await withdraws.ApproveAsync(o1.Id, null, "ok");
        await withdraws.ApproveAsync(o2.Id, null, "ok");
        await withdraws.ApproveAsync(o3.Id, null, "ok");

        // FakeChainGateway 的 TxCountSeed=10 在 3 笔连发期间保持不变，模拟负载均衡 RPC pending 视图不刷新
        var gateway = new FakeChainGateway(head: 600)
        {
            GasPriceGwei = 1m,
            TxCountSeed = 10,
            LatestTxCountSeed = 10,
            ReceiptGasUsed = 55_000,
            ReceiptEffectiveGasPriceWei = BscGasPricePolicy.GweiToWei(1m),
        };
        gateway.SeedTokenBalance(usdt, HotWallet, 10_000m);
        gateway.BnbBalances[HotWallet] = 1m;

        await broadcast.BroadcastQueueAsync(network, gateway, 5);
        gateway.BroadcastCount.ShouldBe(3);
        gateway.Broadcasts.Select(b => b.Nonce).ShouldBe(new[] { 10L, 11L, 12L });

        var h1ForO1 = gateway.Broadcasts[0].Hash;
        var originalStatusChangedAt = DateTime.UtcNow.AddMinutes(-10);
        // 模拟第 1 单（队头 nonce=10）卡池 > 3 分钟；第 2、3 单不处于队头（nonce != latest）
        var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.WithdrawOrders.Where(o => o.Id == o1.Id).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.BroadcastAt, fiveMinutesAgo)
                .SetProperty(o => o.StatusChangedAt, originalStatusChangedAt));
            await db.ChainTxAttempts.Where(a => a.RefId == o1.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.BroadcastAt, fiveMinutesAgo));
            await db.WithdrawOrders.Where(o => o.Id == o2.Id || o.Id == o3.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.BroadcastAt, fiveMinutesAgo));
            await uow.CompleteAsync();
        }

        await broadcast.RecoverStuckBroadcastingAsync(network, gateway);

        // 只有队头 o1(nonce=10) 发出同 nonce 抬价替换；非队头 o2/o3 保持不动
        gateway.BroadcastCount.ShouldBe(4);
        var replaceO1 = gateway.Broadcasts[3];
        replaceO1.Nonce.ShouldBe(10L);
        replaceO1.Hash.ShouldNotBe(h1ForO1);
        gateway.BroadcastGasPrices[replaceO1.Hash]
            .ShouldBeGreaterThanOrEqualTo(gateway.BroadcastGasPrices[h1ForO1] * 110 / 100 + 1);

        using (var check1 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var reloaded = await db.WithdrawOrders.AsNoTracking().SingleAsync(o => o.Id == o1.Id);
            reloaded.TxHash.ShouldBe(replaceO1.Hash);
            (reloaded.StatusChangedAt - originalStatusChangedAt).Duration().ShouldBeLessThan(TimeSpan.FromSeconds(1),
                "同 nonce 加速不应重置硬超时起点 StatusChangedAt");
        }

        // 旧候选 H1 先打包上链 → RecoverStuck 跨全部候选认领 H1 完成订单并记录 H1 GasFeeLog
        gateway.HasReceipt = true;
        gateway.ReceiptHashes = [h1ForO1];
        gateway.ReceiptStatusOverride = 1;

        await broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        using var check2 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db2 = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var completed = await db2.WithdrawOrders.AsNoTracking().SingleAsync(o => o.Id == o1.Id);
        completed.Status.ShouldBe(WithdrawStatus.Completed);
        completed.TxHash.ShouldBe(h1ForO1);
        var fee = await db2.GasFeeLogs.AsNoTracking().SingleAsync(g => g.RefId == o1.Id);
        fee.TxHash.ShouldBe(h1ForO1);
    }

    // ── T6 (P1-3): 钱包轮换重置基线 + 自转净额为 0 + 畸形字节码 & 节点分歧防误熔断 ──

    [Fact]
    public async Task T6_HotWallet_Rotation_SelfTransfer_Conservation_And_Corroborated_GetCode()
    {
        // A. 畸形字节码不应被 HasDeployedCode 误判为有代码
        BscGasPricePolicy.HasDeployedCode("error").ShouldBeFalse();
        BscGasPricePolicy.HasDeployedCode("0x1").ShouldBeFalse();
        BscGasPricePolicy.HasDeployedCode("0x0000").ShouldBeFalse();
        BscGasPricePolicy.HasDeployedCode("0xef0100deadbeef").ShouldBeTrue();

        // B. 多节点 AnkrBscChainGateway 上节点 #1 返回委托代码、节点 #2 返回 "0x" → Disagreement=true, Confirmed=false
        using var handler = new MultiRpcHandler(url =>
        {
            var code = url.Contains("rpc1") ? "0xef01001234567890abcdef1234567890abcdef12345678" : "0x";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, result = code })),
            };
        });
        using var http = new HttpClient(handler);
        var multiGateway = new AnkrBscChainGateway(
            http, ["https://rpc1.example.test", "https://rpc2.example.test"], [], 100, 0, 0,
            NullLogger<AnkrBscChainGateway>.Instance);
        var (confirmed, _, disagreement) = await multiGateway.VerifyDeployedCodeAsync(HotWallet);
        confirmed.ShouldBeFalse();
        disagreement.ShouldBeTrue();

        // C. 钱包轮换重置 Checkpoint + 自转归集记 Self 且不掩盖同窗真实缺口
        var (network, usdt) = await ArrangeSeededWithdrawNetworkAsync();
        var watch = GetRequiredService<HotWalletWatchManager>();
        var settingManager = GetRequiredService<ISettingManager>();
        var settings = GetRequiredService<ISettingProvider>();
        await settingManager.SetGlobalAsync(CollectionSettings.Withdraw.AutoHaltOnAnomaly, "true");

        var gateway = new FakeChainGateway(head: 500);
        gateway.SeedTokenBalance(usdt, HotWallet, 10_000m);
        await watch.RunRoundAsync(network, gateway, 15, CancellationToken.None); // 基线 485

        gateway.Head = 505; // safeHead = 490, 记录 Checkpoint(W1 = 10,000)
        await watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);
        (await settings.IsTrueAsync(CollectionSettings.Withdraw.Halt)).ShouldBeFalse();

        // 合法轮换至 W2（余额仅 2,000 USDT），且拨回 checkpoint 时间 → 应重设基线而不拉响 Halt
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.Networks.Where(n => n.Id == network.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.WithdrawVaultAddress, RotatedWallet));
            await db.HotWalletWatchStates.Where(s => s.TokenId == usdt.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastCheckpointAt, DateTime.UtcNow.AddMinutes(-10)));
            await uow.CompleteAsync();
        }
        network.WithdrawVaultAddress = RotatedWallet;
        gateway.SeedTokenBalance(usdt, RotatedWallet, 2_000m);
        gateway.Head = 510;
        await watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);
        (await settings.IsTrueAsync(CollectionSettings.Withdraw.Halt)).ShouldBeFalse("热钱包轮换应重设基线而非误熔断");

        // 同窗出现 500 USDT 自转 + 真实被盗减少 500 USDT（实余 1,500）→ 自转记 Self(净额 0)，真实缺口仍必检出并熔断
        var selfHash = "0x" + new string('7', 64);
        gateway.Logs[usdt.ContractAddress] =
        [
            new(selfHash, 0, RotatedWallet, RotatedWallet, "500000000000000000000", 500),
        ];
        using (var uow2 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.HotWalletWatchStates.Where(s => s.TokenId == usdt.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastCheckpointAt, DateTime.UtcNow.AddMinutes(-10)));
            await uow2.CompleteAsync();
        }
        gateway.SeedTokenBalance(usdt, RotatedWallet, 1_500m);
        gateway.Head = 520; // safeHead = 505
        await watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        using (var check = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var selfTxn = await db.HotWalletTxns.AsNoTracking().SingleAsync(t => t.TxHash == selfHash);
            selfTxn.Direction.ShouldBe(HotWalletDirection.Self);
        }
        (await settings.IsTrueAsync(CollectionSettings.Withdraw.Halt)).ShouldBeTrue("自转不得掩盖同窗真实余额缺口");
    }

    // ── T7 (P1-4): PurgeStuckNonceAsync 锁对齐 + 活单占用防误杀门禁 ─────────────────

    [Fact]
    public async Task T7_PurgeStuckNonce_Uses_Target_Specific_Lock_And_Enforces_Live_Owner_Guards()
    {
        var (network, _) = await ArrangeSeededWithdrawNetworkAsync();
        var (token, _) = await ReadTokenAsync(network.Id);
        var (_, _, _, watched) = await SeedQueuedTaskAsync(network, token, 20m, 2);

        var gateway = new FakeChainGateway(head: 500)
        {
            GasPriceGwei = 1m,
            LatestTxCountSeed = 14,
            TxCountSeed = 15,
        };
        var lockRecorder = new RecordingDistributedLock(GetRequiredService<IAbpDistributedLock>());
        using var http = new HttpClient();
        var admin = CreateAdminWithGatewayAndLock(gateway, http, lockRecorder);
        using var principal = UseAdminPrincipal();

        // A. HotWallet 存在 Broadcasting 活单占用 nonce=14 → 未拉 Halt 与 ForceOverride 时必须拒绝
        var balances = GetRequiredService<CollectionBalanceManager>();
        var poster = GetRequiredService<CollectionLedgerPoster>();
        var withdraws = GetRequiredService<WithdrawManager>();
        await balances.CreditAsync(User1, CollectionCurrency.Usdt, 500m, $"dep-{Guid.NewGuid()}");
        await poster.PostOpeningAsync(User1, CollectionCurrency.Usdt, 500m);
        var order = await withdraws.SubmitAsync(User1, CollectionCurrency.Usdt, Counterparty, 20m);
        await withdraws.ApproveAsync(order.Id, null, "ok");
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.WithdrawOrders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, WithdrawStatus.Broadcasting)
                .SetProperty(o => o.BroadcastSenderAddress, HotWallet)
                .SetProperty(o => o.NonceUsed, 14L)
                .SetProperty(o => o.TxHash, "0x" + new string('8', 64)));
            await uow.CompleteAsync();
        }

        await Should.ThrowAsync<BusinessException>(() => admin.PurgeStuckNonceAsync(new PurgeStuckNonceInput
        {
            Target = "WithdrawHotWallet",
            Nonce = 14,
            Reason = "blocked without halt+override",
        }));

        // 仅 ForceOverride 但未拉 Halt 仍拒绝
        await Should.ThrowAsync<BusinessException>(() => admin.PurgeStuckNonceAsync(new PurgeStuckNonceInput
        {
            Target = "WithdrawHotWallet",
            Nonce = 14,
            ForceOverrideActiveOwner = true,
            Reason = "still blocked without halt",
        }));

        // 拉响 Halt + ForceOverride 才放行，且持有 WithdrawBroadcastLockKey
        await GetRequiredService<ISettingManager>().SetGlobalAsync(CollectionSettings.Withdraw.Halt, "true");
        var hotRes = await admin.PurgeStuckNonceAsync(new PurgeStuckNonceInput
        {
            Target = "WithdrawHotWallet",
            Nonce = 14,
            ForceOverrideActiveOwner = true,
            Reason = "emergency override",
        });
        hotRes.Nonce.ShouldBe(14L);
        lockRecorder.AcquiredKeys.Last().ShouldBe(CollectionConsts.WithdrawBroadcastLockKey);

        // B. GasSponsor 清理必须持有 SweepLockKey，且有活跃 Fueling 占 nonce=14 时须 ForceOverride
        using (var uow2 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.SweepTasks.Where(t => t.Address == watched).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.Status, SweepTaskStatus.Fueling)
                .SetProperty(t => t.GasNonceUsed, 14L)
                .SetProperty(t => t.GasTxHash, "0x" + new string('9', 64)));
            await uow2.CompleteAsync();
        }

        await Should.ThrowAsync<BusinessException>(() => admin.PurgeStuckNonceAsync(new PurgeStuckNonceInput
        {
            Target = "GasSponsor",
            Nonce = 14,
            Reason = "blocked live fuel",
        }));

        var gasRes = await admin.PurgeStuckNonceAsync(new PurgeStuckNonceInput
        {
            Target = "GasSponsor",
            Nonce = 14,
            ForceOverrideActiveOwner = true,
            Reason = "override stuck fuel",
        });
        gasRes.Nonce.ShouldBe(14L);
        lockRecorder.AcquiredKeys.Last().ShouldBe(CollectionConsts.SweepLockKey);
    }

    // ── T8 (P2-1 + P2-2): Watchdog 跨实例 DB 持久化 + 单块 -32000 熔断防风暴 ──────────

    [Fact]
    public async Task T8_SponsorWatch_Persists_Across_Instances_And_SingleBlock_InternalError_Circuits_Fast()
    {
        // A. Pod A 在 T0 观测到 pending>latest；6 分钟后全新的 Pod B (不同 SweepManager 实例) 仍能按 DB 状态触发自转
        var (network, _, gasAddress) = await SeedCustomNetworkAsync();
        var gateway = new FakeChainGateway(head: 500)
        {
            GasPriceGwei = 1m,
            LatestTxCountSeed = 22,
            TxCountSeed = 23,
        };
        var clockA = new VolOClockWrapper(DateTime.UtcNow);
        var podA = NewSweepManager(clockA);
        await podA.RunSponsorHeadWatchdogAsync(network, gateway, CancellationToken.None);
        gateway.BroadcastCount.ShouldBe(0);

        var clockB = new VolOClockWrapper(clockA.UtcNow.AddMinutes(6));
        var podB = NewSweepManager(clockB);
        await podB.RunSponsorHeadWatchdogAsync(network, gateway, CancellationToken.None);
        gateway.BroadcastCount.ShouldBe(1);
        gateway.Broadcasts.Single().To.ToLowerInvariant().ShouldBe(gasAddress);
        gateway.Broadcasts.Single().Nonce.ShouldBe(22L);

        // B. 128 个监听地址遇到通用 -32000 "internal server error" 必须快速熔断（<= 4 次 HTTP 请求），禁止拆分风暴
        var callCount = 0;
        var watched = Enumerable.Range(1, 128)
            .Select(i => "0x" + i.ToString("x40"))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var services = BscDepositProviderClientTests.FakeTransport(_ =>
        {
            Interlocked.Increment(ref callCount);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    error = new { code = -32000, message = "internal server error" },
                })),
            };
        });
        var source = new BscDepositProviderClient(services.GetRequiredService<IHttpRemoteService>())
            .Create(ChainApiKeyProvider.RpcBsc, "key", 1000);

        var ex = await Should.ThrowAsync<DepositScanReadException>(() =>
            source.ReadLogsAsync(UsdtContract, 101, 101, watched, CancellationToken.None));
        ex.Code.ShouldBe(DepositScanFailure.Unavailable);
        callCount.ShouldBeLessThanOrEqualTo(4, "单块通用 -32000 必须在 2 次分批失败后快速熔断切源");
    }

    private async Task<(ColToken Token, ColNetwork Network)> ReadTokenAsync(Guid networkId)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var net = await db.Networks.AsNoTracking().SingleAsync(n => n.Id == networkId);
        var tok = await db.Tokens.AsNoTracking().FirstAsync(t => t.NetworkId == networkId && t.Symbol == "USDT");
        await uow.CompleteAsync();
        return (tok, net);
    }

    private CollectionWithdrawAdminAppService CreateAdminWithGatewayAndLock(
        FakeChainGateway gateway, HttpClient http, IAbpDistributedLock distributedLock)
    {
        var factory = new TestGatewayFactory(http, GetRequiredService<IStringEncryptionService>(), gateway);
        return new CollectionWithdrawAdminAppService(
            GetRequiredService<WithdrawManager>(),
            GetRequiredService<CollectionLedgerPoster>(),
            GetRequiredService<WithdrawGuard>(),
            GetRequiredService<WithdrawBroadcastManager>(),
            factory,
            GetRequiredService<IDbContextProvider<CollectionDbContext>>(),
            GetRequiredService<ISettingProvider>(),
            GetRequiredService<ISettingManager>(),
            distributedLock,
            GetRequiredService<ChainKeyProvider>(),
            GetRequiredService<IStringEncryptionService>(),
            GetRequiredService<ChainAlertService>())
        {
            LazyServiceProvider = GetRequiredService<CollectionWithdrawAdminAppService>().LazyServiceProvider,
        };
    }

    private IDisposable UseAdminPrincipal() =>
        GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(
            new ClaimsIdentity(new[]
            {
                new Claim(AbpClaimTypes.UserId, AdminId.ToString()),
                new Claim(AbpClaimTypes.UserName, "admin"),
            }, "Testing")));

    private sealed class MultiRpcHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(respond(request.RequestUri!.AbsoluteUri));
    }

    private sealed class RecordingDistributedLock(IAbpDistributedLock inner) : IAbpDistributedLock
    {
        public List<string> AcquiredKeys { get; } = new();

        public async Task<IAbpDistributedLockHandle?> TryAcquireAsync(
            string name, TimeSpan timeout = default, CancellationToken cancellationToken = default)
        {
            AcquiredKeys.Add(name);
            return await inner.TryAcquireAsync(name, timeout, cancellationToken);
        }
    }

    private sealed class TestGatewayFactory(
        HttpClient http, IStringEncryptionService encryptor, FakeChainGateway gateway)
        : ChainGatewayFactory(http, encryptor, "", NullLoggerFactory.Instance)
    {
        public override Task<IBscChainGateway> CreateAsync(CollectionDbContext db, ColNetwork network, int cap)
            => Task.FromResult<IBscChainGateway>(gateway);
    }

    private sealed class VolOClockWrapper(DateTime initialUtc) : Volo.Abp.Timing.IClock
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
