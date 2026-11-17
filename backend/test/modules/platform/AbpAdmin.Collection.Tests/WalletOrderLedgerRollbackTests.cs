using System;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Collection.Settings;
using AbpAdmin.EntityFrameworkCore;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ordering;
using AbpAdmin.Ordering.Data;
using AbpAdmin.Ordering.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Modularity;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

[DependsOn(typeof(CollectionTestModule), typeof(AbpAdmin.Ordering.AbpAdminOrderingModule))]
public class WalletOrderLedgerTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var database = global::AbpAdmin.AbpAdminTestPg.GetDatabase(typeof(WalletOrderLedgerTestModule).Assembly);
        database.MigrateOnce("OrderingDbContext", () =>
        {
            using var dbContext = new OrderingDbContext(
                new DbContextOptionsBuilder<OrderingDbContext>()
                    .UseNpgsql(database.ConnectionString, npgsql =>
                    {
                        npgsql.MigrationsHistoryTable(AbpAdmin.Ordering.OrderingConsts.SchemaHistoryTable);
                    })
                    .Options);
            dbContext.Database.Migrate();
        });
    }
}

public class WalletOrderLedgerRollbackTests : AbpAdminTestBase<WalletOrderLedgerTestModule>
{
    [Fact]
    public async Task Injected_Failure_Rolls_Back_Order_Wallet_And_Ledger()
    {
        var userId = Guid.NewGuid();
        var balances = GetRequiredService<CollectionBalanceManager>();
        var poster = GetRequiredService<CollectionLedgerPoster>();
        await balances.CreditAsync(userId, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid():N}");
        await poster.PostOpeningAsync(userId, CollectionCurrency.Usdt, 100m);
        await GetRequiredService<ISettingManager>()
            .SetGlobalAsync(CollectionSettings.WalletLedger.Enforce, "true");

        var orderId = Guid.NewGuid();
        await Should.ThrowAsync<InvalidOperationException>(async () =>
        {
            using var uow = GetRequiredService<IUnitOfWorkManager>().Begin(requiresNew: true, isTransactional: true);
            var ordering = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
            await ordering.Orders.AddAsync(new OrdOrder(orderId)
            {
                OrderNo = "ORD-ROLLBACK",
                UserId = userId,
                OrderType = OrderType.Business,
                Subject = "rollback",
                Amount = 10m,
                Currency = "USD",
                Status = OrderStatus.PendingPayment,
                ExpiresAt = DateTime.UtcNow.AddHours(1),
                CreationTime = DateTime.UtcNow,
            });
            await ordering.SaveChangesAsync();
            await balances.DebitAsync("order", orderId, userId, CollectionCurrency.Usdt, 10m);
            throw new InvalidOperationException("injected");
        });

        using var read = GetRequiredService<IUnitOfWorkManager>().Begin();
        var orders = await GetRequiredService<IDbContextProvider<OrderingDbContext>>().GetDbContextAsync();
        var wallet = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var ledger = await GetRequiredService<IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
        (await orders.Orders.AnyAsync(order => order.Id == orderId)).ShouldBeFalse();
        (await wallet.Balances.SingleAsync(row => row.UserId == userId)).Balance.ShouldBe(100m);
        (await wallet.BalanceTxns.AnyAsync(txn => txn.BusinessId == orderId)).ShouldBeFalse();
        (await ledger.Vouchers.AnyAsync(voucher => voucher.BizType == FinanceBizTypes.OrderWallet)).ShouldBeFalse();
        var overview = await GetRequiredService<AbpAdmin.Ledger.Services.FinOverviewAppService>().GetAsync();
        overview.CurrencyTotals.ShouldNotBeNull();
        overview.IsBalanced.ShouldBeTrue();
        await read.CompleteAsync();
    }
}
