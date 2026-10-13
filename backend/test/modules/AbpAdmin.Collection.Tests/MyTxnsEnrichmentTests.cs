using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Users;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 用户流水（GetMyTxns）的富化口径：充值行按「幂等键 = 进款单 Id」回填链上凭证 TxHash
/// （用户对账要能看到链上转账），BusinessType/BusinessId/RefTxnId 原样暴露（前端关联订单用），
/// 时间范围过滤生效。余额与贷记本身的状态机在 ChainAcceptanceTests / BalanceManagerTests。
/// </summary>
public class MyTxnsEnrichmentTests : CollectionTestBase
{
    private Guid Me => GetRequiredService<ICurrentUser>().Id!.Value;

    [Fact]
    public async Task Deposit_Row_Carries_TxHash_And_Fields_Time_Filter_Works()
    {
        var depositOrderId = Guid.NewGuid();
        var debitTxnId = Guid.NewGuid();
        var debitBusinessId = Guid.NewGuid();
        var txHash = "0xtxn-" + Guid.NewGuid().ToString("N");

        await UowAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var networkId = Guid.NewGuid();
            await dbContext.Networks.AddAsync(new ColNetwork(networkId)
            {
                Name = "BSC-TXN-" + Guid.NewGuid().ToString("N")[..8],
                ChainId = 97,
                CreationTime = DateTime.UtcNow,
            });
            await dbContext.SaveChangesAsync();
            var token = new ColToken(Guid.NewGuid())
            {
                NetworkId = networkId, Symbol = "USDT", ContractAddress = "0xtxn-usdt",
                Decimals = 18, MinDeposit = "10", IsEnabled = true, CreationTime = DateTime.UtcNow,
            };
            await dbContext.Tokens.AddAsync(token);
            await dbContext.DepositOrders.AddAsync(new ColDepositOrder(depositOrderId)
            {
                NetworkId = networkId, TokenId = token.Id, UserId = Me, AddressId = Guid.NewGuid(),
                ToAddress = "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8],
                FromAddress = "0x" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N")[..8],
                TxHash = txHash, LogIndex = 1, RawAmount = "100000000",
                Amount = 1m, BlockNumber = 100, Status = DepositOrderStatus.Swept,
                CreationTime = DateTime.UtcNow,
            });
            // 充值贷记行：幂等键 = 进款单 Id（CreditCoreAsync/SweepManager 的写入口径）
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ColBalanceTxns" ("Id", "UserId", "Currency", "Action", "Amount", "BalanceAfter", "IdempotencyKey", "BusinessType", "BusinessId", "RefTxnId", "ReversedAt", "CreationTime")
                VALUES ({Guid.NewGuid()}, {Me}, {(int)CollectionCurrency.Usdt}, {BalanceActions.DepositCredit}, {1m}, {1m}, {depositOrderId.ToString("D")}, {CollectionBusinessTypes.TopUp}, {depositOrderId}, NULL, NULL, {DateTime.UtcNow})
                """);
            // 非 Guid 幂等键的充值贷记（历史直连测试数据形态）：不回填凭证、不误连
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ColBalanceTxns" ("Id", "UserId", "Currency", "Action", "Amount", "BalanceAfter", "IdempotencyKey", "BusinessType", "BusinessId", "RefTxnId", "ReversedAt", "CreationTime")
                VALUES ({Guid.NewGuid()}, {Me}, {(int)CollectionCurrency.Usdt}, {BalanceActions.DepositCredit}, {7m}, {7m}, {"dep-legacy-" + Guid.NewGuid().ToString("N")[..8]}, {CollectionBusinessTypes.TopUp}, {Guid.NewGuid()}, NULL, NULL, {DateTime.UtcNow})
                """);
            // 扣款行：BusinessType=Order（OrderingConsts.OrderBusinessType 的口径），金额为负
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ColBalanceTxns" ("Id", "UserId", "Currency", "Action", "Amount", "BalanceAfter", "IdempotencyKey", "BusinessType", "BusinessId", "RefTxnId", "ReversedAt", "CreationTime")
                VALUES ({debitTxnId}, {Me}, {(int)CollectionCurrency.Usdt}, {BalanceActions.BusinessDebit}, {-0.5m}, {0.5m}, {"biz-" + debitBusinessId.ToString("D")}, {"Order"}, {debitBusinessId}, NULL, NULL, {DateTime.UtcNow})
                """);
            await dbContext.SaveChangesAsync();
        });

        var page = await UowAsync(async () =>
        {
            var service = GetRequiredService<CollectionBalanceAppService>();
            return await service.GetMyTxnsAsync(0, 50);
        });

        page.TotalCount.ShouldBe(3);
        var deposit = page.Items.Single(t => t.Action == BalanceActions.DepositCredit && t.TxHash != null);
        deposit.TxHash.ShouldBe(txHash);
        // 非 Guid 幂等键的充值行：无凭证可回填（不误连、不抛）——原断言空洞（挂在扣款行上）
        page.Items.Count(t => t.Action == BalanceActions.DepositCredit && t.TxHash == null).ShouldBe(1);
        deposit.BusinessType.ShouldBe(CollectionBusinessTypes.TopUp);
        deposit.BusinessId.ShouldBe(depositOrderId);
        var debit = page.Items.Single(t => t.Action == BalanceActions.BusinessDebit);
        debit.BusinessType.ShouldBe("Order");
        debit.BusinessId.ShouldBe(debitBusinessId);
        debit.TxHash.ShouldBeNull();

        // 时间过滤：上界在未来 1 小时内 → 两行都在；下界推到 1 小时后 → 空
        var within = await UowAsync(async () =>
        {
            var service = GetRequiredService<CollectionBalanceAppService>();
            return await service.GetMyTxnsAsync(0, 50, fromTime: DateTime.UtcNow.AddHours(-1), toTime: DateTime.UtcNow.AddHours(1));
        });
        within.TotalCount.ShouldBe(3);
        var future = await UowAsync(async () =>
        {
            var service = GetRequiredService<CollectionBalanceAppService>();
            return await service.GetMyTxnsAsync(0, 50, fromTime: DateTime.UtcNow.AddHours(1));
        });
        future.TotalCount.ShouldBe(0);

        // 边界等式契约：fromTime 含端点（>=）、toTime 不含端点（<）。
        // 边界行放未来 1 小时——已有行都建在“现在”，fromTime 取过去时刻会把它们全部包含，无法隔离
        var boundary = DateTime.UtcNow.AddHours(1);
        await UowAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ColBalanceTxns" ("Id", "UserId", "Currency", "Action", "Amount", "BalanceAfter", "IdempotencyKey", "BusinessType", "BusinessId", "RefTxnId", "ReversedAt", "CreationTime")
                VALUES ({Guid.NewGuid()}, {Me}, {(int)CollectionCurrency.Usdt}, {BalanceActions.DepositCredit}, {1m}, {1m}, {"boundary-" + Guid.NewGuid().ToString("N")[..8]}, {CollectionBusinessTypes.TopUp}, {Guid.NewGuid()}, NULL, NULL, {boundary})
                """);
        });
        var atFrom = await UowAsync(async () =>
        {
            var service = GetRequiredService<CollectionBalanceAppService>();
            return await service.GetMyTxnsAsync(0, 50, fromTime: boundary);
        });
        atFrom.TotalCount.ShouldBe(1);   // >=：等于下界的行计入
        var atTo = await UowAsync(async () =>
        {
            var service = GetRequiredService<CollectionBalanceAppService>();
            return await service.GetMyTxnsAsync(0, 50, toTime: boundary);
        });
        atTo.TotalCount.ShouldBe(3);     // <：等于上界的行不计入
    }

    private async Task<T> UowAsync<T>(Func<Task<T>> action)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        var result = await action();
        await uow.CompleteAsync();
        return result;
    }

    private async Task UowAsync(Func<Task> action)
    {
        using var uow = GetRequiredService<IUnitOfWorkManager>().Begin();
        await action();
        await uow.CompleteAsync();
    }
}
