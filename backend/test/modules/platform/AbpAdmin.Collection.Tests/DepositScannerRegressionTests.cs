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
        GetRequiredService<IUnitOfWorkManager>(), GetRequiredService<IGuidGenerator>())
    {
        LazyServiceProvider = GetRequiredService<IAbpLazyServiceProvider>(),
    };

    private static Task ScanAsync(DepositScanManager scanner, ColNetwork network, ColToken token, IBscChainGateway gateway) =>
        scanner.RunTokenRoundAsync(network, token, gateway, 12, 50, 2000, TimeSpan.FromMinutes(10), 0, CancellationToken.None);

    private async Task<(ColNetwork Network, ColToken Token)> SeedAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var now = GetRequiredService<IClock>().Now;
        var network = new ColNetwork(Guid.NewGuid())
        {
            Name = "SCANNER-" + Guid.NewGuid().ToString("N")[..8],
            ChainId = 97,
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
