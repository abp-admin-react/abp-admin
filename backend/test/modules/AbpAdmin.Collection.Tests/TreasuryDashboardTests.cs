using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 会员资金查询 + 资金看板聚合锁：
/// 会员端——余额两币不缺项（无行补 0）、流水按 Seq 倒序分页；
/// 看板——状态桶计数/金额、今日 UTC 口径、双侧手续费（Swept/Completed）、
/// 在途提现净额、用户余额合计按币。
/// </summary>
public class TreasuryDashboardTests : CollectionTestBase
{
    [Fact]
    public async Task Member_Balances_Fill_Both_Currencies_And_Txns_Page()
    {
        var userId = Guid.NewGuid();
        await InsertBalanceAsync(userId, CollectionCurrency.Usdt, 100m, 5m);

        var admin = GetRequiredService<CollectionBalanceAdminAppService>();
        var balances = await admin.GetMemberBalancesAsync(userId);
        balances.Items.Count.ShouldBe(2); // USDT + USDC 不缺项
        var usdt = balances.Items.Single(b => b.Currency == "Usdt");
        usdt.Balance.ShouldBe("100");
        usdt.Frozen.ShouldBe("5");
        usdt.Total.ShouldBe("105");
        var usdc = balances.Items.Single(b => b.Currency == "Usdc");
        usdc.Balance.ShouldBe("0"); // 无行补 0

        var txns = await admin.GetMemberTxnsAsync(userId, 0, 50);
        txns.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Dashboard_Aggregates_Status_Today_Fees_And_User_Balances()
    {
        var user1 = Guid.NewGuid();
        var user2 = Guid.NewGuid();
        await InsertBalanceAsync(user1, CollectionCurrency.Usdt, 100m, 5m);
        await InsertBalanceAsync(user2, CollectionCurrency.Usdt, 40m, 0m);
        await InsertBalanceAsync(user2, CollectionCurrency.Usdc, 7m, 0m);

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await DbAsync();
            // 今日三笔（Swept 含手续费 / Confirmed 未挂任务=大额门持有 / 昨日一笔不计入今日）
            dbContext.DepositOrders.Add(new ColDepositOrder(Guid.NewGuid())
            {
                NetworkId = Guid.NewGuid(),
                TokenId = Guid.NewGuid(),
                UserId = user1,
                AddressId = Guid.NewGuid(),
                TxHash = "0xd1",
                LogIndex = 0,
                FromAddress = "0xa",
                ToAddress = "0xb",
                RawAmount = "1000000000000000000",
                Amount = 1m,
                Fee = 0.001m,
                ActualAmount = 0.999m,
                BlockNumber = 100,
                BlockTime = DateTime.UtcNow,
                Status = DepositOrderStatus.Swept,
                CreationTime = DateTime.UtcNow,
            });
            dbContext.DepositOrders.Add(new ColDepositOrder(Guid.NewGuid())
            {
                NetworkId = Guid.NewGuid(),
                TokenId = Guid.NewGuid(),
                UserId = user2,
                AddressId = Guid.NewGuid(),
                TxHash = "0xd2",
                LogIndex = 0,
                FromAddress = "0xa",
                ToAddress = "0xc",
                RawAmount = "2000000000000000000",
                Amount = 2m,
                Fee = 0.002m,
                ActualAmount = 1.998m,
                BlockNumber = 101,
                BlockTime = DateTime.UtcNow,
                Status = DepositOrderStatus.Confirmed, // 未挂任务 = 大额门持有
                CreationTime = DateTime.UtcNow,
            });
            // 昨日单：今日桶必须排除（UTC 日界锁——删掉 Where 过滤此断言即红）
            dbContext.DepositOrders.Add(new ColDepositOrder(Guid.NewGuid())
            {
                NetworkId = Guid.NewGuid(),
                TokenId = Guid.NewGuid(),
                UserId = user2,
                AddressId = Guid.NewGuid(),
                TxHash = "0xd3",
                LogIndex = 0,
                FromAddress = "0xa",
                ToAddress = "0xc",
                RawAmount = "4000000000000000000",
                Amount = 4m,
                Fee = 0.004m,
                ActualAmount = 3.996m,
                BlockNumber = 99,
                BlockTime = DateTime.UtcNow,
                Status = DepositOrderStatus.Swept,
                CreationTime = DateTime.UtcNow.AddDays(-2),
            });
            dbContext.WithdrawOrders.Add(new ColWithdrawOrder(Guid.NewGuid())
            {
                UserId = user1,
                Currency = CollectionCurrency.Usdt,
                NetworkName = "BSC",
                ToAddress = "0x4444444444444444444444444444444444444444",
                Amount = 10m,
                Fee = 0.04m,
                ActualAmount = 9.96m,
                FeeRate = 0.004m,
                Status = WithdrawStatus.Completed,
                CompleteTime = DateTime.UtcNow,
                StatusChangedAt = DateTime.UtcNow,
                CreationTime = DateTime.UtcNow,
            });
            dbContext.WithdrawOrders.Add(new ColWithdrawOrder(Guid.NewGuid())
            {
                UserId = user2,
                Currency = CollectionCurrency.Usdt,
                NetworkName = "BSC",
                ToAddress = "0x4444444444444444444444444444444444444444",
                Amount = 20m,
                Fee = 0.08m,
                ActualAmount = 19.92m,
                FeeRate = 0.004m,
                Status = WithdrawStatus.Processing, // 在途
                StatusChangedAt = DateTime.UtcNow,
                CreationTime = DateTime.UtcNow,
            });
            await dbContext.SaveChangesAsync();
            await uow.CompleteAsync();
        }

        // 看板端点已移入 CollectionBalanceAdminAppService（收款查看权限位，与菜单/路由一致）
        var admin = GetRequiredService<CollectionBalanceAdminAppService>();
        var dashboard = await admin.GetDashboardAsync();

        dashboard.DepositByStatus.Single(b => b.Status == nameof(DepositOrderStatus.Swept))
            .Count.ShouldBe(2);
        dashboard.HeldDepositCount.ShouldBe(1); // Confirmed 且未挂任务
        dashboard.DepositFeeTotal.ShouldBe("0.005"); // 只算 Swept（0.001 + 昨日 0.004）
        dashboard.WithdrawByStatus.Single(b => b.Status == nameof(WithdrawStatus.Completed))
            .Count.ShouldBe(1);
        dashboard.WithdrawFeeTotal.ShouldBe("0.04"); // 只算 Completed
        dashboard.WithdrawInFlightAmount.ShouldBe("19.92"); // Processing 净额
        dashboard.DepositTodayCount.ShouldBe(2); // 昨日单不计入（UTC 日界）
        dashboard.WithdrawTodayCount.ShouldBe(2);

        var usdtBucket = dashboard.UserBalances.Single(b => b.Currency == "Usdt");
        usdtBucket.Balance.ShouldBe("140"); // 100 + 40
        usdtBucket.Frozen.ShouldBe("5");
        dashboard.UserBalances.Single(b => b.Currency == "Usdc").Balance.ShouldBe("7");
    }

    private async Task InsertBalanceAsync(Guid userId, CollectionCurrency currency, decimal balance, decimal frozen)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var dbContext = await DbAsync();
        dbContext.Balances.Add(new ColBalance(Guid.NewGuid())
        {
            UserId = userId,
            Currency = currency,
            Balance = balance,
            Frozen = frozen,
            UpdatedAt = DateTime.UtcNow,
        });
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
    }

    private async Task<CollectionDbContext> DbAsync()
        => await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
}
