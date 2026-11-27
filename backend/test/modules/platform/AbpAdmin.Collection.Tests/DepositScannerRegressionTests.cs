using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Guids;
using Volo.Abp.Security.Encryption;
using Volo.Abp.Timing;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

public class DepositScannerRegressionTests : CollectionTestBase
{
    private const string WatchAddress = "0xa000000000000000000000000000000000000001";
    private const string FromAddress = "0xb000000000000000000000000000000000000002";

    [Fact]
    public async Task Incomplete_Fallback_Retains_Cursor_And_Replay_Neither_Duplicates_Nor_Realerts()
    {
        var (network, token) = await SeedAsync();
        var gateway = NewGateway(token, [], false);
        var overflowing = new TokenTransferLog("0xABC", 4, FromAddress, WatchAddress, new string('9', 30), 101);
        var replay = overflowing with { TxHash = "0xabc" };
        var morePages = true;
        var requestedPages = new List<int>();
        gateway.GetTransfersForAddressAsync(token.ContractAddress, WatchAddress, 101, 101,
                Arg.Any<int>(), 200, Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var page = call.ArgAt<int>(4);
                requestedPages.Add(page);
                return new AddressTransferPage([page == 1 ? overflowing : replay], morePages);
            });
        var alerts = new RecordingAlerts(GetRequiredService<IDistributedEventBus>());
        var scanner = NewScanner(alerts);

        await ScanAsync(scanner, network, token, gateway);

        requestedPages.ShouldBe(Enumerable.Range(1, 10));
        var first = await ReadAsync(token.Id);
        first.Cursor.ShouldBe(100L);
        first.Orders.Single().TxHash.ShouldBe("0xabc");
        first.Orders.Single().Status.ShouldBe(DepositOrderStatus.PrecisionOverflow);
        alerts.Titles.Count(title => title == "精度放不下的转入").ShouldBe(1);

        morePages = false;
        await ScanAsync(scanner, network, token, gateway);

        var second = await ReadAsync(token.Id);
        second.Cursor.ShouldBe(101L);
        second.Orders.Single().Id.ShouldBe(first.Orders.Single().Id);
        alerts.Titles.Count(title => title == "精度放不下的转入").ShouldBe(1);
        var notification = Fakes.FakeChainPushNotifier.Instance.Deposits.ShouldHaveSingleItem();
        notification.DepositOrderId.ShouldBe(first.Orders.Single().Id);
        notification.Status.ShouldBe(nameof(DepositOrderStatus.PrecisionOverflow));
    }

    [Fact]
    public async Task Confirmed_Deposit_And_New_Queue_Notify_Once_Even_When_The_Deposit_Push_Fails()
    {
        var (network, token) = await SeedAsync(autoCreditMax: 100);
        var log = new TokenTransferLog("0xabc", 1, FromAddress, WatchAddress, "20000000000000000000", 101);
        var gateway = NewGateway(token, [log], true);
        var notifier = Fakes.FakeChainPushNotifier.Instance;
        notifier.RejectDepositPush = true;
        var scanner = NewScanner(new RecordingAlerts(GetRequiredService<IDistributedEventBus>()));
        await ScanAsync(scanner, network, token, gateway);
        await ScanAsync(scanner, network, token, gateway);
        var snapshot = await ReadAsync(token.Id);
        snapshot.Cursor.ShouldBe(101);
        var order = snapshot.Orders.ShouldHaveSingleItem();
        var deposit = notifier.Deposits.ShouldHaveSingleItem();
        deposit.DepositOrderId.ShouldBe(order.Id);
        deposit.Status.ShouldBe(nameof(DepositOrderStatus.Confirmed));
        var sweep = notifier.Sweeps.ShouldHaveSingleItem();
        sweep.SweepTaskId.ShouldBe(order.SweepTaskId!.Value);
        sweep.Status.ShouldBe(nameof(SweepTaskStatus.Queued));
    }

    [Fact]
    public async Task Out_Of_Range_Logs_Are_Rejected_Without_Advancing_The_Cursor()
    {
        var (network, token) = await SeedAsync();
        var inRange = new TokenTransferLog("0xABC", 1, FromAddress, WatchAddress, "1", 101);
        var outOfRange = inRange with { TxHash = "0xdef", BlockNumber = 102 };
        var gateway = NewGateway(token, [outOfRange], true);
        gateway.GetTransfersForAddressAsync(token.ContractAddress, WatchAddress, 101, 101, 1, 200,
                Arg.Any<CancellationToken>())
            .Returns(new AddressTransferPage([inRange, outOfRange], Incomplete: false));
        var scanner = NewScanner(new RecordingAlerts(GetRequiredService<IDistributedEventBus>()));

        await ScanAsync(scanner, network, token, gateway);

        var result = await ReadAsync(token.Id);
        result.Cursor.ShouldBe(100L);
        result.Orders.Single().TxHash.ShouldBe("0xabc");
    }

    [Fact]
    public async Task Ordered_Failover_Persists_Replay_Across_Restart_While_Continuing_Forward()
    {
        var (network, token) = await SeedAsync();
        var ankr = new RecordingDepositSource(requiresReconciliation: false);
        var explorer = new RecordingDepositSource(requiresReconciliation: true);
        var health = new DepositScanProviderHealth();
        var ankrId = Guid.NewGuid();
        var explorerId = Guid.NewGuid();
        var first = new DepositScanProvider(ankrId, "Ankr", ankr, health);
        var second = new DepositScanProvider(explorerId, "Etherscan", explorer, health);
        var seen = new TokenTransferLog("0xaaa", 1, FromAddress, WatchAddress, "1", 101);
        var missed = seen with { TxHash = "0xbbb", BlockNumber = 102 };
        var current = seen with { TxHash = "0xccc", BlockNumber = 103 };
        var alerts = new RecordingAlerts(GetRequiredService<IDistributedEventBus>());

        ankr.ReadHead = () => 114;
        ankr.ReadLogs = (_, _) => throw new DepositScanReadException(DepositScanFailure.Quota);
        explorer.ReadHead = () => 114;
        explorer.ReadLogs = (from, to) => (from, to) switch
        {
            (101, 102) => [seen],
            (102, 102) => [],
            _ => throw new InvalidOperationException($"Unexpected scan window {from}..{to}"),
        };

        await ScanAsync(NewScanner(alerts), network, token, [first, second]);

        var backup = await ReadProviderStateAsync(token.Id);
        backup.Cursor.ShouldBe(102);
        backup.From.ShouldBe(101);
        backup.To.ShouldBe(102);
        backup.Provider.ShouldBe("Etherscan");
        backup.Failure.ShouldBe(DepositScanFailure.Quota);
        (await ReadAsync(token.Id)).Orders.Single().TxHash.ShouldBe(seen.TxHash);

        await ScanAsync(NewScanner(alerts), network, token, [first, second]);
        ankr.HeadCalls.ShouldBe(1);

        // Cooldowns are process-local. The database survives the restart and still knows what needs replaying.
        var restartedHealth = new DepositScanProviderHealth();
        ankr.ReadHead = () => 115;
        ankr.ReadLogs = (from, to) => (from, to) switch
        {
            (101, 102) => [seen, missed],
            (103, 103) => [current],
            _ => throw new InvalidOperationException($"Unexpected scan window {from}..{to}"),
        };

        await ScanAsync(NewScanner(alerts), network, token, [first with { Health = restartedHealth }, second with { Health = restartedHealth }]);

        explorer.HeadCalls.ShouldBe(2);
        var recovered = await ReadProviderStateAsync(token.Id);
        recovered.Cursor.ShouldBe(103);
        recovered.From.ShouldBeNull();
        recovered.To.ShouldBeNull();
        recovered.Provider.ShouldBe("Ankr");
        recovered.Failure.ShouldBeNull();
        (await ReadAsync(token.Id)).Orders.Select(order => order.TxHash).Order()
            .ShouldBe(new[] { seen.TxHash, missed.TxHash, current.TxHash });
    }

    [Fact]
    public async Task Invalid_Raw_Amount_Does_Not_Claim_An_Identity_Or_Advance_The_Cursor()
    {
        var (network, token) = await SeedAsync();
        var log = new TokenTransferLog("0xaba", 0, FromAddress, WatchAddress, "-1", 101);
        var source = new RecordingDepositSource(false) { ReadHead = () => 113, ReadLogs = (_, _) => [log] };
        var scanner = NewScanner(new RecordingAlerts(GetRequiredService<IDistributedEventBus>()));
        await ScanAsync(scanner, network, token, [new(Guid.NewGuid(), "RPC", source, new DepositScanProviderHealth())]);
        (await ReadAsync(token.Id)).Orders.ShouldBeEmpty();
        var state = await ReadProviderStateAsync(token.Id);
        state.Cursor.ShouldBe(100);
        state.Failure.ShouldBe(DepositScanFailure.EvidenceInvalid);

        log = log with { RawAmount = "1" };
        await ScanAsync(scanner, network, token, [new(Guid.NewGuid(), "RPC", source, new DepositScanProviderHealth())]);
        var recovered = await ReadAsync(token.Id);
        recovered.Cursor.ShouldBe(101);
        recovered.Orders.Single().RawAmount.ShouldBe("1");
    }

    [Fact]
    public async Task Contradictory_Receipt_On_Replay_Stops_Confirmation_And_Retains_Chain_Evidence()
    {
        var (network, token) = await SeedAsync();
        var indexedLog = new TokenTransferLog("0xeff", 2, FromAddress, WatchAddress, "11000000000000000000", 101);
        var rpcLog = indexedLog with { RawAmount = "12000000000000000000" };
        var indexed = new RecordingDepositSource(true) { ReadHead = () => 113, ReadLogs = (_, _) => [indexedLog] };
        var rpc = new RecordingDepositSource(false) { ReadHead = () => 113, ReadLogs = (_, _) => [rpcLog] };
        var health = new DepositScanProviderHealth();
        DepositScanProvider[] providers = [new(Guid.NewGuid(), "Etherscan", indexed, health), new(Guid.NewGuid(), "RPC", rpc, health)];
        var scanner = NewScanner(new RecordingAlerts(GetRequiredService<IDistributedEventBus>()));

        var error = await Should.ThrowAsync<InvalidOperationException>(() => ScanAsync(scanner, network, token, providers));
        error.Message.ShouldContain("0xeff#2");
        var disputed = await ReadAsync(token.Id);
        disputed.Cursor.ShouldBe(100);
        var order = disputed.Orders.Single();
        order.RawAmount.ShouldBe(indexedLog.RawAmount);
        order.Status.ShouldBe(DepositOrderStatus.PendingConfirm);
        order.SweepTaskId.ShouldBeNull();

        rpcLog = indexedLog with { RawAmount = "0" + indexedLog.RawAmount, From = FromAddress.ToUpperInvariant() };
        await ScanAsync(scanner, network, token, providers);
        var recovered = await ReadAsync(token.Id);
        recovered.Cursor.ShouldBe(101);
        recovered.Orders.Single().Id.ShouldBe(order.Id);
        recovered.Orders.Single().Status.ShouldBe(DepositOrderStatus.Confirmed);
    }

    [Fact]
    public async Task Reincluded_Transfer_Keeps_The_Later_Height_Until_Settled_And_Preserves_The_Credited_Record()
    {
        var (network, token) = await SeedAsync();
        var log = new TokenTransferLog("0xace", 3, FromAddress, WatchAddress, "11000000000000000000", 101);
        var head = 113L;
        var rpc = new RecordingDepositSource(false) { ReadHead = () => head, ReadLogs = (_, _) => [log] };
        DepositScanProvider[] providers = [new(Guid.NewGuid(), "RPC", rpc, new DepositScanProviderHealth())];
        var scanner = NewScanner(new RecordingAlerts(GetRequiredService<IDistributedEventBus>()));
        await ScanAsync(scanner, network, token, providers);
        var id = (await ReadAsync(token.Id)).Orders.Single().Id;

        head = 114;
        log = log with { BlockNumber = 102 };
        await ScanAsync(scanner, network, token, providers);
        var moved = (await ReadAsync(token.Id)).Orders.Single();
        moved.Id.ShouldBe(id);
        moved.BlockNumber.ShouldBe(102);

        // A replay may then observe the original height. The later, more conservative height wins.
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.Tokens.Where(t => t.Id == token.Id).ExecuteUpdateAsync(s => s.SetProperty(t => t.LastScannedBlock, 100L));
            await uow.CompleteAsync();
        }
        log = log with { BlockNumber = 101 };
        await ScanAsync(scanner, network, token, providers);
        (await ReadAsync(token.Id)).Orders.Single().BlockNumber.ShouldBe(102);

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.DepositOrders.Where(o => o.Id == id).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, DepositOrderStatus.Swept));
            await uow.CompleteAsync();
        }
        head = 115;
        log = log with { BlockNumber = 103 };
        await ScanAsync(scanner, network, token, providers);
        var settled = await ReadAsync(token.Id);
        settled.Cursor.ShouldBe(103);
        var stored = settled.Orders.Single();
        stored.Id.ShouldBe(id);
        stored.BlockNumber.ShouldBe(102);
        stored.Status.ShouldBe(DepositOrderStatus.Swept);
    }

    [Fact]
    public async Task Legacy_Rpc_Remains_Available_Until_Managed_Ankr_Reads_Logs()
    {
        var (network, token) = await SeedAsync();
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true))
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var row = await db.Networks.SingleAsync(item => item.Id == network.Id);
            row.LegacyDepositRpcCipher = "retained";
            row.PreferManagedScanProviders = true;
            await uow.CompleteAsync();
        }

        var health = new DepositScanProviderHealth();
        var unavailable = new RecordingDepositSource(true) { ReadHead = () => throw new DepositScanReadException(ProviderCredentialStatus.PlanRequired) };
        var legacy = new RecordingDepositSource(false) { ReadHead = () => 113 };
        var scanner = NewScanner(new RecordingAlerts(GetRequiredService<IDistributedEventBus>()));
        var oldRpc = new DepositScanProvider(network.Id, "Legacy", legacy, health, IsLegacy: true);
        await ScanAsync(scanner, network, token, [new(Guid.NewGuid(), "Etherscan", unavailable, health), oldRpc]);
        (await ReadNetwork()).LegacyDepositRpcCipher.ShouldBe("retained");
        (await ReadProviderStateAsync(token.Id)).Provider.ShouldBe("Legacy");

        var verified = new RecordingDepositSource(false) { ReadHead = () => 113 };
        await ScanAsync(scanner, network, token, [new(Guid.NewGuid(), "Ankr", verified, health), oldRpc]);
        (await ReadNetwork()).LegacyDepositRpcCipher.ShouldBeNull();
        (await ReadProviderStateAsync(token.Id)).Provider.ShouldBe("Ankr");

        async Task<ColNetwork> ReadNetwork()
        {
            using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await db.Networks.AsNoTracking().SingleAsync(item => item.Id == network.Id);
        }
    }

    [Fact]
    public async Task Etherscan_First_Continues_Scanning_And_Uses_Lower_Rpc_For_Replay()
    {
        var (network, token) = await SeedAsync();
        var indexed = new RecordingDepositSource(requiresReconciliation: true) { ReadHead = () => 114 };
        var rpc = new RecordingDepositSource(requiresReconciliation: false) { ReadHead = () => 113 };
        var missed = new TokenTransferLog("0xaaa", 0, FromAddress, WatchAddress, "1", 101);
        rpc.ReadLogs = (from, to) => (from, to) switch
        {
            (101, 101) => [missed],
            (102, 102) => [],
            _ => throw new InvalidOperationException($"Unexpected replay {from}..{to}"),
        };
        var health = new DepositScanProviderHealth();
        DepositScanProvider[] providers = [new(Guid.NewGuid(), "Etherscan", indexed, health), new(Guid.NewGuid(), "Ankr", rpc, health)];
        var scanner = NewScanner(new RecordingAlerts(GetRequiredService<IDistributedEventBus>()));

        await ScanAsync(scanner, network, token, providers);

        var partial = await ReadProviderStateAsync(token.Id);
        partial.Provider.ShouldBe("Etherscan");
        partial.Cursor.ShouldBe(102);
        partial.From.ShouldBe(102);
        partial.To.ShouldBe(102);

        rpc.ReadHead = () => 114;
        await ScanAsync(scanner, network, token, providers);

        var recovered = await ReadProviderStateAsync(token.Id);
        recovered.Provider.ShouldBe("Etherscan");
        recovered.Cursor.ShouldBe(102);
        recovered.From.ShouldBeNull();
        recovered.To.ShouldBeNull();
        (await ReadAsync(token.Id)).Orders.Single().TxHash.ShouldBe(missed.TxHash);
    }

    [Fact]
    public async Task Idle_Head_Read_Does_Not_Replace_The_Last_Proven_Scanner()
    {
        var (network, token) = await SeedAsync();
        var health = new DepositScanProviderHealth();
        var original = new RecordingDepositSource(false) { ReadHead = () => 113 };
        var newProvider = new RecordingDepositSource(false) { ReadHead = () => 113,
            ReadLogs = (_, _) => throw new DepositScanReadException(DepositScanFailure.Quota) };
        var scanner = NewScanner(new RecordingAlerts(GetRequiredService<IDistributedEventBus>()));
        await ScanAsync(scanner, network, token, [new(Guid.NewGuid(), "Original", original, health)]);
        var before = await ReadProviderStateAsync(token.Id);

        await ScanAsync(scanner, network, token, [new(Guid.NewGuid(), "New", newProvider, health)]);

        var after = await ReadProviderStateAsync(token.Id);
        after.Provider.ShouldBe("Original");
        after.ProviderAt.ShouldBe(before.ProviderAt);
        after.Cursor.ShouldBe(before.Cursor);
    }

    [Fact]
    public async Task Total_Failure_Preserves_Last_Success_And_Cursor()
    {
        var (network, token) = await SeedAsync();
        var ankr = new RecordingDepositSource(requiresReconciliation: false) { ReadHead = () => 113 };
        var health = new DepositScanProviderHealth();
        var first = new DepositScanProvider(Guid.NewGuid(), "Main", ankr, health);
        var alerts = new RecordingAlerts(GetRequiredService<IDistributedEventBus>());
        var scanner = NewScanner(alerts);
        await ScanAsync(scanner, network, token, [first]);
        var success = await ReadProviderStateAsync(token.Id);

        ankr.ReadHead = () => throw new DepositScanReadException(DepositScanFailure.Timeout);
        await ScanAsync(scanner, network, token, [first]);
        await ScanAsync(scanner, network, token, [first]);

        var failed = await ReadProviderStateAsync(token.Id);
        failed.Cursor.ShouldBe(101);
        failed.Provider.ShouldBe("Main");
        failed.ProviderAt.ShouldBe(success.ProviderAt);
        failed.Failure.ShouldBe(DepositScanFailure.Timeout);
        (await ReadAsync(token.Id)).Orders.ShouldBeEmpty();
        alerts.Titles.ShouldBe(new[] { "充值扫链服务商全部不可用" });
    }

    [Fact]
    public async Task Provider_Factory_Uses_Enabled_Network_Rows_In_Order_And_Leaves_Outgoing_Rpc_Separate()
    {
        var (network, _) = await SeedAsync();
        var factory = GetRequiredService<BscDepositScanProviderFactory>();
        var encryptor = GetRequiredService<IStringEncryptionService>();
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        network = await db.Networks.SingleAsync(row => row.Id == network.Id);
        network.RpcCipher = HdKeyService.Protect("https://rpc.example.com/transaction-only", "test-master-key", encryptor);
        var ankr = AddProvider(ChainApiKeyProvider.RpcBsc, 10, new string('a', 64));
        var explorer = AddProvider(ChainApiKeyProvider.EtherscanV2, 5, new string('b', 34));
        AddProvider(ChainApiKeyProvider.RpcBsc, 1, new string('c', 64)).IsEnabled = false;
        await db.SaveChangesAsync();

        var available = await factory.CreateAsync(db, network, CancellationToken.None);

        available.Select(provider => provider.Name).ShouldBe(new[] { "Etherscan", "Ankr" });
        available.All(provider => provider.Source != null).ShouldBeTrue();

        ankr.IsEnabled = explorer.IsEnabled = false;
        await db.SaveChangesAsync();
        (await factory.CreateAsync(db, network, CancellationToken.None)).ShouldBeEmpty();
        await uow.CompleteAsync();

        ColScanProvider AddProvider(ChainApiKeyProvider provider, int sortOrder, string key)
        {
            var row = new ColScanProvider(Guid.NewGuid())
            {
                Provider = provider,
                SortOrder = sortOrder,
                KeyHash = key,
                KeyCipher = HdKeyService.Protect(key, "test-master-key", encryptor),
                IsEnabled = true,
            };
            row.Networks.Add(new ColScanProviderNetwork { ProviderId = row.Id, NetworkId = network.Id });
            db.ScanProviders.Add(row);
            return row;
        }
    }

    private static IBscChainGateway NewGateway(ColToken token, IReadOnlyList<TokenTransferLog> logs, bool complete)
    {
        var gateway = Substitute.For<IBscChainGateway>();
        gateway.GetLatestBlockNumberAsync(Arg.Any<CancellationToken>()).Returns(113L);
        gateway.GetTransferLogsAsync(token.ContractAddress, 101, 101, Arg.Any<CancellationToken>())
            .Returns((logs, complete));
        return gateway;
    }

    private DepositScanManager NewScanner(ChainAlertService alerts) => new(
        GetRequiredService<IDbContextProvider<CollectionDbContext>>(), alerts, GetRequiredService<IClock>(),
        GetRequiredService<IUnitOfWorkManager>(), GetRequiredService<IGuidGenerator>(), ServiceProvider)
    {
        LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>(),
    };

    private static Task ScanAsync(DepositScanManager scanner, ColNetwork network, ColToken token, IBscChainGateway gateway) =>
        scanner.RunTokenRoundAsync(network, token, gateway, 12, 50, 2000, TimeSpan.FromMinutes(10), 0, CancellationToken.None);

    private static Task ScanAsync(DepositScanManager scanner, ColNetwork network, ColToken token, IReadOnlyList<DepositScanProvider> providers) =>
        scanner.RunTokenRoundAsync(network, token, providers, 12, 50, 2000, TimeSpan.FromMinutes(10), 0, CancellationToken.None);

    private async Task<(ColNetwork Network, ColToken Token)> SeedAsync(decimal autoCreditMax = 0)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var now = GetRequiredService<IClock>().Now;
        var network = new ColNetwork(Guid.NewGuid())
        {
            Name = "SCANNER-" + Guid.NewGuid().ToString("N")[..8],
            ChainId = 97,
            AutoCreditMaxAmount = autoCreditMax,
            CreationTime = now,
        };
        var token = new ColToken(Guid.NewGuid())
        {
            NetworkId = network.Id,
            Symbol = "USDT",
            ContractAddress = "0xc000000000000000000000000000000000000003",
            Decimals = 18,
            MinDeposit = "10",
            LastScannedBlock = 100,
            IsEnabled = true,
            CreationTime = now,
        };
        await db.Networks.AddAsync(network);
        await db.Tokens.AddAsync(token);
        await db.DepositAddresses.AddAsync(new ColDepositAddress(Guid.NewGuid())
        {
            NetworkId = network.Id,
            UserId = Guid.NewGuid(),
            Address = WatchAddress,
            AddressIndex = 2,
            CreationTime = now,
        });
        await uow.CompleteAsync();
        return (network, token);
    }

    private async Task<(long? Cursor, List<ColDepositOrder> Orders)> ReadAsync(Guid tokenId)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var token = await db.Tokens.SingleAsync(t => t.Id == tokenId);
        var orders = await db.DepositOrders.Where(o => o.TokenId == tokenId).ToListAsync();
        await uow.CompleteAsync();
        return (token.LastScannedBlock, orders);
    }

    private async Task<(long? Cursor, long? From, long? To, string? Provider, DateTime? ProviderAt, string? Failure)> ReadProviderStateAsync(Guid tokenId)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var token = await db.Tokens.SingleAsync(t => t.Id == tokenId);
        await uow.CompleteAsync();
        return (token.LastScannedBlock, token.ScanReplayFromBlock, token.ScanReplayToBlock, token.LastScanProvider,
            token.LastScanProviderAt, token.LastScanFailure);
    }

    private sealed class RecordingDepositSource(bool requiresReconciliation) : IBscDepositScanSource
    {
        public bool RequiresReconciliation { get; } = requiresReconciliation;
        public Func<long> ReadHead { get; set; } = () => throw new InvalidOperationException("Unexpected head call");
        public Func<long, long, IReadOnlyList<TokenTransferLog>> ReadLogs { get; set; } = (_, _) => [];
        public int HeadCalls { get; private set; }

        public Task<long> GetHeadAsync(CancellationToken ct)
        {
            HeadCalls++;
            return Task.FromResult(ReadHead());
        }

        public Task<IReadOnlyList<TokenTransferLog>> ReadLogsAsync(string contract, long from, long to,
            IReadOnlySet<string> watched, CancellationToken ct) => Task.FromResult(ReadLogs(from, to));
    }

    private sealed class RecordingAlerts(IDistributedEventBus eventBus)
        : ChainAlertService([], eventBus, NullLogger<ChainAlertService>.Instance)
    {
        public List<string> Titles { get; } = [];

        public override Task RaiseAsync(string? prefix, string title, string detail)
        {
            Titles.Add(title);
            return Task.CompletedTask;
        }
    }
}
