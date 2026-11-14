using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.Modularity;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

public class SweepSettlementRollbackTests : AbpAdminTestBase<SweepSettlementRollbackTestModule>
{
    [Fact]
    public async Task Interrupted_After_First_Flushed_Voucher_Rolls_Back_And_Replays_Confirmed_Sweep_Once()
    {
        var (network, token, task, deposit) = await SeedBroadcastAsync();
        var bus = GetRequiredService<IDistributedEventBus>();
        var observed = new List<BalanceCreditedEto>();
        using var subscription = bus.Subscribe<BalanceCreditedEto>(eventData =>
        {
            if (eventData.UserId == deposit.UserId)
            {
                observed.Add(eventData);
            }
            return Task.CompletedTask;
        });

        var gateway = new FakeChainGateway(head: 500)
        {
            HasReceipt = true,
            ReceiptConfirmationsMet = true,
            ReceiptHashes = [task.TxHash!],
        };
        gateway.Logs[token.ContractAddress] =
        [
            new TokenTransferLog(task.TxHash!, 0, task.Address, token.VaultAddress!, "20000000000000000000", 400),
        ];

        using (var failedRound = ServiceProvider.CreateScope())
        {
            var failure = failedRound.ServiceProvider.GetRequiredService<SweepPostingFailure>();
            failure.Inject = true;
            await Should.ThrowAsync<OperationCanceledException>(() => RunRoundAsync(failedRound, gateway));
            failure.FlushedBalance.ShouldBe(19m);
            failure.FlushedWalletTxnId.ShouldNotBeNull();
            failure.FlushedPayVoucherId.ShouldNotBeNull();
            failure.FeeVoucherCount.ShouldBe(0);
        }

        await AssertSettlementAsync(committed: false);
        observed.ShouldBeEmpty();
        gateway.Broadcasts.ShouldBeEmpty();

        using (var restarted = ServiceProvider.CreateScope())
        {
            await RunRoundAsync(restarted, gateway);
            await RunRoundAsync(restarted, gateway);
        }

        var credit = await AssertSettlementAsync(committed: true);
        var published = observed.Single();
        credit.ShouldBe(published.BalanceTxnId);
        published.UserId.ShouldBe(deposit.UserId);
        published.Amount.ShouldBe("19");
        gateway.Broadcasts.ShouldBeEmpty();

        Task RunRoundAsync(IServiceScope scope, IBscChainGateway chain) => scope.ServiceProvider
            .GetRequiredService<SweepManager>()
            .RunTokenRoundAsync(network, token, chain, 12, null, 0.005m,
                TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(120), 0.02m, CancellationToken.None);

        async Task<Guid?> AssertSettlementAsync(bool committed)
        {
            using var scope = ServiceProvider.CreateScope();
            var services = scope.ServiceProvider;
            using var read = services.GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
            var collection = await services.GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var ledger = await services.GetRequiredService<IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
            var orderState = await collection.DepositOrders.AsNoTracking().SingleAsync(row => row.Id == deposit.Id);
            var queueState = await collection.SweepTasks.AsNoTracking().SingleAsync(row => row.Id == task.Id);
            orderState.Status.ShouldBe(committed ? DepositOrderStatus.Swept : DepositOrderStatus.Sweeping);
            queueState.Status.ShouldBe(committed ? SweepTaskStatus.Swept : SweepTaskStatus.Sweeping);
            queueState.TxHash.ShouldBe(task.TxHash);

            var depositKey = BalanceIdempotencyKeys.ForDepositOrder(deposit.Id);
            var balance = await collection.Balances.AsNoTracking().SingleOrDefaultAsync(row => row.UserId == deposit.UserId);
            var walletCredit = await collection.BalanceTxns.AsNoTracking().SingleOrDefaultAsync(row => row.IdempotencyKey == depositKey);
            var vouchers = await ledger.Vouchers.AsNoTracking()
                .Where(row => row.BizRefId == depositKey && row.BizType == FinanceBizTypes.Deposit).ToListAsync();
            if (committed)
            {
                balance.ShouldNotBeNull().Balance.ShouldBe(19m);
                walletCredit.ShouldNotBeNull().Amount.ShouldBe(19m);
                vouchers.Count.ShouldBe(2);
                vouchers.Single(row => row.Phase == FinanceBizTypes.Phases.Pay).Amount.ShouldBe(20m);
                vouchers.Single(row => row.Phase == FinanceBizTypes.Phases.Fee).Amount.ShouldBe(1m);
            }
            else
            {
                balance.ShouldBeNull();
                walletCredit.ShouldBeNull();
                vouchers.ShouldBeEmpty();
            }

            await read.CompleteAsync();
            return walletCredit?.Id;
        }
    }

    private async Task<(ColNetwork Network, ColToken Token, ColSweepTask Task, ColDepositOrder Deposit)> SeedBroadcastAsync()
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true);
        var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var now = DateTime.UtcNow;
        var userId = Guid.NewGuid();
        var addressId = Guid.NewGuid();
        var watched = "0x" + new string('a', 40);
        var network = new ColNetwork(Guid.NewGuid()) { Name = "BSC-SETTLEMENT", ChainId = 97, CreationTime = now };
        var token = new ColToken(Guid.NewGuid())
        {
            NetworkId = network.Id, Symbol = "USDT", ContractAddress = "0x" + new string('b', 40),
            Decimals = 18, MinDeposit = "10", IsEnabled = true, VaultAddress = "0x" + new string('c', 40), CreationTime = now,
        };
        var task = new ColSweepTask(Guid.NewGuid())
        {
            NetworkId = network.Id, TokenId = token.Id, UserId = userId, AddressId = addressId,
            Address = watched, AddressIndex = 2, Amount = 20m, Status = SweepTaskStatus.Sweeping,
            TxHash = "0x" + new string('d', 64), NonceUsed = 41, BroadcastVaultAddress = token.VaultAddress,
            BroadcastAt = now, CreationTime = now, StatusChangedAt = now,
        };
        var deposit = new ColDepositOrder(Guid.NewGuid())
        {
            NetworkId = network.Id, TokenId = token.Id, UserId = userId, AddressId = addressId,
            ToAddress = watched, FromAddress = "0x" + new string('e', 40), TxHash = "0x" + new string('f', 64),
            BlockNumber = 120, RawAmount = "20000000000000000000", Amount = 20m, Fee = 1m, ActualAmount = 19m,
            Status = DepositOrderStatus.Sweeping, SweepTaskId = task.Id, CreationTime = now,
        };
        db.Networks.Add(network);
        db.Tokens.Add(token);
        db.DepositAddresses.Add(new ColDepositAddress(addressId)
        {
            NetworkId = network.Id, UserId = userId, Address = watched, AddressIndex = 2, CreationTime = now,
        });
        db.SweepTasks.Add(task);
        db.DepositOrders.Add(deposit);
        await uow.CompleteAsync();
        return (network, token, task, deposit);
    }
}

[DependsOn(typeof(CollectionTestModule))]
public class SweepSettlementRollbackTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddScoped<SweepPostingFailure>();
        context.Services.Replace(ServiceDescriptor.Transient<CollectionLedgerPoster, InterruptibleSweepLedgerPoster>());
    }
}

public sealed class SweepPostingFailure
{
    public bool Inject { get; set; }
    public decimal? FlushedBalance { get; set; }
    public Guid? FlushedWalletTxnId { get; set; }
    public Guid? FlushedPayVoucherId { get; set; }
    public int? FeeVoucherCount { get; set; }
}

public class InterruptibleSweepLedgerPoster(ILedgerIntegrationService ledger, SweepPostingFailure failure,
    IDbContextProvider<CollectionDbContext> collectionProvider, IDbContextProvider<LedgerDbContext> ledgerProvider)
    : CollectionLedgerPoster(ledger)
{
    public override async Task<Guid> PostDepositCreditAsync(Guid depositOrderId, Guid userId,
        CollectionCurrency currency, decimal amount)
    {
        var voucherId = await base.PostDepositCreditAsync(depositOrderId, userId, currency, amount);
        if (!failure.Inject)
        {
            return voucherId;
        }

        var collection = await collectionProvider.GetDbContextAsync();
        var finance = await ledgerProvider.GetDbContextAsync();
        var depositKey = BalanceIdempotencyKeys.ForDepositOrder(depositOrderId);
        failure.FlushedBalance = await collection.Balances.AsNoTracking()
            .Where(row => row.UserId == userId).Select(row => (decimal?)row.Balance).SingleAsync();
        failure.FlushedWalletTxnId = await collection.BalanceTxns.AsNoTracking()
            .Where(row => row.IdempotencyKey == depositKey).Select(row => (Guid?)row.Id).SingleAsync();
        failure.FlushedPayVoucherId = await finance.Vouchers.AsNoTracking()
            .Where(row => row.Id == voucherId && row.Phase == FinanceBizTypes.Phases.Pay).Select(row => (Guid?)row.Id).SingleAsync();
        failure.FeeVoucherCount = await finance.Vouchers.CountAsync(row => row.BizRefId == depositKey
            && row.BizType == FinanceBizTypes.Deposit && row.Phase == FinanceBizTypes.Phases.Fee);
        throw new OperationCanceledException("Interrupted after PostgreSQL flushed the wallet credit and pay voucher.");
    }
}
