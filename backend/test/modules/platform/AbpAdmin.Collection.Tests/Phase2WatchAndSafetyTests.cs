using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Security.Claims;
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

public class Phase2WatchAndSafetyTests : CollectionTestBase
{
    private const string Mnemonic = "test test test test test test test test test test test test";
    private const string MasterKey = "test-master-key";
    private const string HotWalletKey = "0x1111111111111111111111111111111111111111111111111111111111111111";
    private const string HotWallet = "0x19e7e376e7c213b7e7e7e46cc70a5dd086daff2a";
    private const string SecondWalletKey = "0x2222222222222222222222222222222222222222222222222222222222222222";
    private static readonly string SecondWallet = new Nethereum.Signer.EthECKey(SecondWalletKey).GetPublicAddress().ToLowerInvariant();
    private const string Counterparty = "0x3333333333333333333333333333333333333333";
    private const string UsdtContract = "0x55d398326f99059fF775485246999027B3197955";
    private static readonly Guid User1 = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa2");
    private static readonly Guid AdminId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbb2");

    private HotWalletWatchManager Watch => GetRequiredService<HotWalletWatchManager>();
    private WithdrawGuard Guard => GetRequiredService<WithdrawGuard>();
    private WithdrawManager Withdraws => GetRequiredService<WithdrawManager>();
    private WithdrawBroadcastManager Broadcast => GetRequiredService<WithdrawBroadcastManager>();
    private CollectionBalanceManager Balances => GetRequiredService<CollectionBalanceManager>();
    private CollectionLedgerPoster Poster => GetRequiredService<CollectionLedgerPoster>();
    private ISettingProvider Settings => GetRequiredService<ISettingProvider>();
    private ISettingManager SettingManager => GetRequiredService<ISettingManager>();

    private async Task<(ColNetwork Network, ColToken Usdt)> ArrangeSeededNetworkWithWalletAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var encryptor = GetRequiredService<IStringEncryptionService>();
        var network = await db.Networks.OrderBy(n => n.CreationTime).FirstAsync();
        network.WithdrawVaultAddress = HotWallet;
        network.WithdrawVaultKeyCipher = await Guard.ProtectKeyAsync(HotWalletKey);
        network.MnemonicCipher = HdKeyService.Protect(Mnemonic, MasterKey, encryptor);
        var usdt = await db.Tokens.FirstAsync(t => t.NetworkId == network.Id && t.Symbol == "USDT");
        await db.SaveChangesAsync();
        await uow.CompleteAsync();
        return (network, usdt);
    }

    private CollectionWithdrawAdminAppService CreateAdminWithFakeGateway(FakeChainGateway gateway, HttpClient http)
    {
        var factory = new TestGatewayFactory(http, GetRequiredService<IStringEncryptionService>(), gateway);
        var app = new CollectionWithdrawAdminAppService(
            Withdraws, Poster, Guard, Broadcast, factory,
            GetRequiredService<IDbContextProvider<CollectionDbContext>>(),
            Settings, SettingManager, GetRequiredService<IAbpDistributedLock>(),
            GetRequiredService<ChainKeyProvider>(),
            GetRequiredService<IStringEncryptionService>(),
            GetRequiredService<ChainAlertService>())
        {
            LazyServiceProvider = GetRequiredService<CollectionWithdrawAdminAppService>().LazyServiceProvider,
        };
        return app;
    }

    private IDisposable UseAdminPrincipal() =>
        GetRequiredService<ICurrentPrincipalAccessor>().Change(new ClaimsPrincipal(
            new ClaimsIdentity(new[]
            {
                new Claim(AbpClaimTypes.UserId, AdminId.ToString()),
                new Claim(AbpClaimTypes.UserName, "admin"),
            }, "Testing")));

    [Fact]
    public async Task HotWalletWatch_Prefers_Rpc_GetLogs_Without_Calling_Etherscan()
    {
        var (network, usdt) = await ArrangeSeededNetworkWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500);
        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        var outHash = "0x" + new string('a', 64);
        var inHash = "0x" + new string('b', 64);
        gateway.Head = 520;
        gateway.Logs[usdt.ContractAddress] =
        [
            new(outHash, 0, HotWallet, Counterparty, "5000000000000000000", 490),
            new(inHash, 1, Counterparty, HotWallet, "8000000000000000000", 491),
        ];

        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        gateway.AddressTransferReads.ShouldBeEmpty("RPC 正常时热钱包监控不得依赖 Etherscan");
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var txns = await db.HotWalletTxns.AsNoTracking().ToListAsync();
        txns.Count.ShouldBe(2);
        txns.Single(t => t.TxHash == outHash).Status.ShouldBe(HotWalletTxnStatus.Anomaly);
        txns.Single(t => t.TxHash == inHash).Status.ShouldBe(HotWalletTxnStatus.ExternalIn);
        await uow.CompleteAsync();
    }

    [Fact]
    public async Task HotWallet_With_Eip7702_Delegation_Code_Immediately_Trips_Withdraw_Halt()
    {
        var (network, _) = await ArrangeSeededNetworkWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500);
        gateway.AddressCodes[HotWallet] = "0xef01001234567890abcdef1234567890abcdef12345678";

        (await Settings.IsTrueAsync(CollectionSettings.Withdraw.Halt)).ShouldBeFalse();
        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);
        (await Settings.IsTrueAsync(CollectionSettings.Withdraw.Halt)).ShouldBeTrue();
    }

    [Fact]
    public async Task GasSponsor_With_Eip7702_Delegation_Code_Refuses_Watchdog_SelfTransfer()
    {
        var (network, _) = await ArrangeSeededNetworkWithWalletAsync();
        var gasAddress = HdKeyService.DeriveAddress(Mnemonic, HdKeyService.GasAddressIndex).ToLowerInvariant();
        var gateway = new FakeChainGateway(head: 500)
        {
            LatestTxCountSeed = 5,
            TxCountSeed = 6,
        };
        gateway.AddressCodes[gasAddress] = "0xef0100deadbeefdeadbeefdeadbeefdeadbeefdeadbeef";

        var manager = GetRequiredService<SweepManager>().WithMasterKey(MasterKey);
        await manager.RunSponsorHeadWatchdogAsync(network, gateway, CancellationToken.None);
        gateway.BroadcastCount.ShouldBe(0);
    }

    [Fact]
    public async Task HotWalletWatch_AutoPromotes_Late_Withdraw_TxHash_From_Anomaly_To_Matched()
    {
        var (network, usdt) = await ArrangeSeededNetworkWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500);
        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None); // 基线 485

        var lateHash = "0x" + new string('c', 64);
        var orderId = Guid.NewGuid();
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            db.WithdrawOrders.Add(new ColWithdrawOrder(orderId)
            {
                UserId = User1,
                Currency = CollectionCurrency.Usdt,
                NetworkName = network.Name,
                ToAddress = Counterparty,
                Amount = 10m,
                Fee = 0m,
                ActualAmount = 10m,
                FeeRate = 0m,
                Status = WithdrawStatus.Broadcasting,
                TxHash = null,
                NonceUsed = 9,
                BroadcastSenderAddress = HotWallet,
                StatusChangedAt = DateTime.UtcNow,
                CreationTime = DateTime.UtcNow,
            });
            await uow.CompleteAsync();
        }

        gateway.Head = 510; // safeHead = 495
        gateway.Logs[usdt.ContractAddress] =
        [
            new(lateHash, 0, HotWallet, Counterparty, "10000000000000000000", 490),
        ];
        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        using (var check1 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var row = await db.HotWalletTxns.AsNoTracking().SingleAsync(t => t.TxHash == lateHash);
            row.Status.ShouldBe(HotWalletTxnStatus.Anomaly);
            await check1.CompleteAsync();
        }

        // 迟到认领写回 WithdrawOrder.TxHash，下一轮自动晋升 Anomaly -> Matched
        using (var patch = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.WithdrawOrders.Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.TxHash, lateHash));
            await patch.CompleteAsync();
        }

        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);
        using (var check2 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var row = await db.HotWalletTxns.AsNoTracking().SingleAsync(t => t.TxHash == lateHash);
            row.Status.ShouldBe(HotWalletTxnStatus.Matched);
            row.MatchedWithdrawId.ShouldBe(orderId);
            await check2.CompleteAsync();
        }
    }

    [Fact]
    public async Task HotWalletWatch_Unconfirmed_Peek_Alerts_Only_And_AutoHaltOnAnomaly_Halts_On_Confirmation()
    {
        var (network, usdt) = await ArrangeSeededNetworkWithWalletAsync();
        await SettingManager.SetGlobalAsync(CollectionSettings.Withdraw.AutoHaltOnAnomaly, "true");

        var gateway = new FakeChainGateway(head: 500);
        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None); // 基线 485

        // 未确认块 498 (> safeHead 485) 出现未知出账：仅实时告警，不得提前熔断且游标不动
        var stealHash = "0x" + new string('d', 64);
        gateway.Logs[usdt.ContractAddress] =
        [
            new(stealHash, 0, HotWallet, Counterparty, "50000000000000000000", 498),
        ];

        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        (await Settings.IsTrueAsync(CollectionSettings.Withdraw.Halt)).ShouldBeFalse("未确认区不得直接触发自动停机熔断");
        using (var check = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            (await db.HotWalletTxns.AnyAsync(t => t.TxHash == stealHash)).ShouldBeFalse("未确认区不得写入已确认流水表");
            (await db.HotWalletWatchStates.SingleAsync(s => s.TokenId == usdt.Id)).LastScannedBlock.ShouldBe(485L);
            await check.CompleteAsync();
        }

        // 区块确认推进后（safeHead >= 498），写入 Anomaly 流水并触发 AutoHalt
        gateway.Head = 520; // safeHead = 505
        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        (await Settings.IsTrueAsync(CollectionSettings.Withdraw.Halt)).ShouldBeTrue("已确认异常出账必须触发 AutoHalt");
        using var check2 = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db2 = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var row = await db2.HotWalletTxns.AsNoTracking().SingleAsync(t => t.TxHash == stealHash);
        row.Status.ShouldBe(HotWalletTxnStatus.Anomaly);
        await check2.CompleteAsync();
    }

    [Fact]
    public async Task HotWalletWatch_Balance_Checkpoint_Detects_Unexplained_Drop_And_AutoHalts()
    {
        var (network, usdt) = await ArrangeSeededNetworkWithWalletAsync();
        await SettingManager.SetGlobalAsync(CollectionSettings.Withdraw.AutoHaltOnAnomaly, "true");

        var gateway = new FakeChainGateway(head: 500);
        gateway.SeedTokenBalance(usdt, HotWallet, 1000m);
        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None); // 基线 485

        gateway.Head = 505; // safeHead = 490，触发首个 Checkpoint 记录 1000
        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);
        (await Settings.IsTrueAsync(CollectionSettings.Withdraw.Halt)).ShouldBeFalse();

        // 拨回上次 Checkpoint 时间，同时把实际余额降为 900（链上少了 100 且无对应出站流水）
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.HotWalletWatchStates.Where(s => s.TokenId == usdt.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastCheckpointAt, DateTime.UtcNow.AddMinutes(-10)));
            await uow.CompleteAsync();
        }

        gateway.Head = 510; // safeHead = 495
        gateway.SeedTokenBalance(usdt, HotWallet, 900m);
        await Watch.RunRoundAsync(network, gateway, 15, CancellationToken.None);

        (await Settings.IsTrueAsync(CollectionSettings.Withdraw.Halt)).ShouldBeTrue("Checkpoint 余额出现未解释减少时必须触发 AutoHalt");
    }

    [Fact]
    public async Task GasFeeLog_Records_Confirmed_Sweep_And_Withdraw_Fees_Idempotently()
    {
        var (network, usdt) = await ArrangeSeededNetworkWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500)
        {
            GasPriceGwei = 2m,
            ReceiptGasUsed = 52_000,
            ReceiptEffectiveGasPriceWei = BscGasPricePolicy.GweiToWei(2m),
        };
        gateway.TokenBalances[UsdtContract + ":" + HotWallet] = "10000000000000000000000";
        gateway.BnbBalances[HotWallet] = 1m;

        await Balances.CreditAsync(User1, CollectionCurrency.Usdt, 500m, $"dep-{Guid.NewGuid()}");
        await Poster.PostOpeningAsync(User1, CollectionCurrency.Usdt, 500m);
        var order = await Withdraws.SubmitAsync(User1, CollectionCurrency.Usdt, Counterparty, 50m);
        await Withdraws.ApproveAsync(order.Id, null, "ok");

        await Broadcast.BroadcastQueueAsync(network, gateway, 10);
        var txHash = gateway.Broadcasts.Single().Hash;
        gateway.HasReceipt = true;
        gateway.ReceiptHashes = [txHash];

        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);
        // 再跑一次幂等重放不会重复插行
        await Broadcast.RecoverStuckBroadcastingAsync(network, gateway);

        using var check = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var log = await db.GasFeeLogs.AsNoTracking().SingleAsync(g => g.NetworkId == network.Id && g.TxHash == txHash);
        log.Purpose.ShouldBe(ChainTxPurpose.WithdrawPayout);
        log.RefId.ShouldBe(order.Id);
        log.GasUsed.ShouldBe(52_000L);
        log.FeeBnb.ShouldBe(0.000104m); // 52_000 * 2 Gwei = 0.000104 BNB
        await check.CompleteAsync();
    }

    [Fact]
    public async Task SetWithdrawVault_Guards_Against_Broadcasting_Delegation_And_AddressMismatch()
    {
        var (network, _) = await ArrangeSeededNetworkWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500);
        using var http = new HttpClient();
        var admin = CreateAdminWithFakeGateway(gateway, http);
        using var principal = UseAdminPrincipal();

        // 1. ConfirmAddress 不匹配
        await Should.ThrowAsync<BusinessException>(() => admin.SetWithdrawVaultAsync(new SetWithdrawVaultInput
        {
            Address = SecondWallet,
            ConfirmAddress = Counterparty,
            PrivateKey = SecondWalletKey,
        }));

        // 2. 目标地址存在 EIP-7702 字节码
        gateway.AddressCodes[SecondWallet] = "0xef01001111111111111111111111111111111111111111";
        await Should.ThrowAsync<BusinessException>(() => admin.SetWithdrawVaultAsync(new SetWithdrawVaultInput
        {
            Address = SecondWallet,
            ConfirmAddress = SecondWallet,
            PrivateKey = SecondWalletKey,
        }));
        gateway.AddressCodes.Clear();

        // 3. 存在 Broadcasting 在途提现单时禁止轮换热钱包
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            db.WithdrawOrders.Add(new ColWithdrawOrder(Guid.NewGuid())
            {
                UserId = User1,
                Currency = CollectionCurrency.Usdt,
                NetworkName = network.Name,
                ToAddress = Counterparty,
                Amount = 20m,
                Fee = 0m,
                ActualAmount = 20m,
                FeeRate = 0m,
                Status = WithdrawStatus.Broadcasting,
                TxHash = "0x" + new string('7', 64),
                NonceUsed = 1,
                BroadcastSenderAddress = HotWallet,
                StatusChangedAt = DateTime.UtcNow,
                CreationTime = DateTime.UtcNow,
            });
            await uow.CompleteAsync();
        }

        var ex = await Should.ThrowAsync<BusinessException>(() => admin.SetWithdrawVaultAsync(new SetWithdrawVaultInput
        {
            Address = SecondWallet,
            ConfirmAddress = SecondWallet,
            PrivateKey = SecondWalletKey,
        }));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawPendingOutbound);
    }

    [Fact]
    public async Task Admin_GetWalletNonceStatus_And_PurgeStuckNonce_Works_For_Stuck_Head_Only()
    {
        var (network, _) = await ArrangeSeededNetworkWithWalletAsync();
        var gateway = new FakeChainGateway(head: 500)
        {
            LatestTxCountSeed = 14,
            TxCountSeed = 15, // pending(15) > latest(14)
        };
        using var http = new HttpClient();
        var admin = CreateAdminWithFakeGateway(gateway, http);
        using var principal = UseAdminPrincipal();

        var status = await admin.GetWalletNonceStatusAsync();
        status.WithdrawHotWallet.Configured.ShouldBeTrue();
        status.WithdrawHotWallet.Address.ShouldBe(HotWallet);
        status.WithdrawHotWallet.LatestNonce.ShouldBe(14L);
        status.WithdrawHotWallet.PendingNonce.ShouldBe(15L);
        status.WithdrawHotWallet.HasPendingGap.ShouldBeTrue();

        // 非队头 nonce 拒绝清理
        await Should.ThrowAsync<BusinessException>(() => admin.PurgeStuckNonceAsync(new PurgeStuckNonceInput
        {
            Target = "WithdrawHotWallet",
            Nonce = 99,
            Reason = "wrong nonce",
        }));

        // 合法队头 nonce=14 清理：发送 0-BNB 自转并记录 ColChainTxAttempt
        var purged = await admin.PurgeStuckNonceAsync(new PurgeStuckNonceInput
        {
            Target = "WithdrawHotWallet",
            Nonce = 14,
            Reason = "clear stuck head",
        });

        purged.SenderAddress.ShouldBe(HotWallet);
        purged.Nonce.ShouldBe(14L);
        gateway.BroadcastCount.ShouldBe(1);
        var tx = gateway.Broadcasts.Single();
        tx.To.ToLowerInvariant().ShouldBe(HotWallet);
        tx.DataHex.ShouldBe(string.Empty);
        tx.Nonce.ShouldBe(14L);

        using var check = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var attempt = await db.ChainTxAttempts.AsNoTracking()
            .SingleAsync(a => a.NetworkId == network.Id && a.TxHash == purged.TxHash);
        attempt.Purpose.ShouldBe(ChainTxPurpose.SponsorHeadSelfTransfer);
        attempt.Status.ShouldBe(ChainTxAttemptStatus.Broadcasted);
        await check.CompleteAsync();
    }

    private sealed class TestGatewayFactory(
        HttpClient http, IStringEncryptionService encryptor, FakeChainGateway gateway)
        : ChainGatewayFactory(http, encryptor, "", NullLoggerFactory.Instance)
    {
        public override Task<IBscChainGateway> CreateAsync(CollectionDbContext db, ColNetwork network, int cap)
            => Task.FromResult<IBscChainGateway>(gateway);
    }
}
