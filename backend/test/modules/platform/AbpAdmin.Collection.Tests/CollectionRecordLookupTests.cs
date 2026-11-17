using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using AbpAdmin.Collection.Chain;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

public class CollectionRecordLookupTests : CollectionTestBase
{
    [Fact]
    public async Task Deposit_And_Withdrawal_Id_Filters_Find_The_Exact_Older_Record()
    {
        var userId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var newerId = Guid.NewGuid();
        var networkId = Guid.NewGuid();
        var tokenId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var sweepId = Guid.NewGuid();
        var txnId = Guid.NewGuid();

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            InsertOrders(targetId, now.AddDays(-1), WithdrawStatus.Completed);
            InsertOrders(newerId, now, WithdrawStatus.Pending);
            db.SweepTasks.Add(new ColSweepTask(sweepId)
            {
                NetworkId = networkId, TokenId = tokenId, UserId = userId, AddressId = Guid.NewGuid(),
                Address = "0xb", AddressIndex = 2, Amount = 1m, Status = SweepTaskStatus.Swept,
                CreationTime = now.AddDays(-1), StatusChangedAt = now.AddDays(-1)
            });
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ColBalanceTxns" ("Id", "UserId", "Currency", "Action", "Amount", "BalanceAfter", "IdempotencyKey", "BusinessType", "BusinessId", "RefTxnId", "ReversedAt", "CreationTime")
                VALUES ({txnId}, {userId}, {(int)CollectionCurrency.Usdt}, {BalanceActions.DepositCredit}, {1m}, {1m}, {BalanceIdempotencyKeys.ForDepositOrder(targetId)}, {CollectionBusinessTypes.TopUp}, {txnId}, NULL, NULL, {now})
                """);
            await db.SaveChangesAsync();
            await uow.CompleteAsync();

            void InsertOrders(Guid id, DateTime createdAt, WithdrawStatus withdrawalStatus)
            {
                db.DepositOrders.Add(new ColDepositOrder(id)
                {
                    NetworkId = networkId,
                    TokenId = tokenId,
                    UserId = userId,
                    AddressId = Guid.NewGuid(),
                    TxHash = $"0x{id:N}",
                    LogIndex = 0,
                    FromAddress = "0xa",
                    ToAddress = "0xb",
                    RawAmount = "1000000000000000000",
                    Amount = 1m,
                    Status = DepositOrderStatus.Swept,
                    SweepTaskId = id == targetId ? sweepId : null,
                    CreationTime = createdAt,
                });
                db.WithdrawOrders.Add(new ColWithdrawOrder(id)
                {
                    UserId = userId,
                    Currency = CollectionCurrency.Usdt,
                    NetworkName = "BSC",
                    ToAddress = "0x4444444444444444444444444444444444444444",
                    Amount = 10m,
                    ActualAmount = 10m,
                    Status = withdrawalStatus,
                    StatusChangedAt = createdAt,
                    CreationTime = createdAt,
                });
            }
        }

        var deposits = GetRequiredService<CollectionNetworkAdminAppService>();
        var withdrawals = GetRequiredService<CollectionWithdrawAdminAppService>();
        (await deposits.GetDepositOrdersAsync(0, 1)).Items.ShouldHaveSingleItem().Id.ShouldBe(newerId);
        (await withdrawals.GetWithdrawalsAsync(0, 1)).Items.ShouldHaveSingleItem().Id.ShouldBe(newerId);

        var deposit = await deposits.GetDepositOrdersAsync(0, 1, depositId: targetId);
        deposit.TotalCount.ShouldBe(1);
        deposit.Items.ShouldHaveSingleItem().Id.ShouldBe(targetId);
        deposit.Items[0].BalanceTxnId.ShouldBe(txnId);
        deposit.Items[0].SweepTaskId.ShouldBe(sweepId);
        (await deposits.GetDepositOrdersAsync(0, 20, sweepTaskId: sweepId)).Items.ShouldHaveSingleItem().Id.ShouldBe(targetId);
        (await deposits.GetDepositOrdersAsync(0, 20, depositId: newerId, sweepTaskId: sweepId)).Items.ShouldBeEmpty();
        (await deposits.GetDepositOrdersAsync(0, 1, depositId: newerId)).Items.ShouldHaveSingleItem().BalanceTxnId.ShouldBeNull();
        (await deposits.GetSweepTasksAsync(0, 1, sweepTaskId: sweepId)).Items.ShouldHaveSingleItem().Id.ShouldBe(sweepId);
        (await deposits.GetSweepTasksAsync(0, 1, status: nameof(SweepTaskStatus.Queued), sweepTaskId: sweepId)).Items.ShouldBeEmpty();
        (await deposits.GetSweepTasksAsync(0, 1, sweepTaskId: Guid.NewGuid())).Items.ShouldBeEmpty();

        // Even an exact persisted idempotency key cannot establish a link across account owners.
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.DepositOrders.Where(o => o.Id == targetId).ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, Guid.NewGuid()));
            await uow.CompleteAsync();
        }
        (await deposits.GetDepositOrdersAsync(0, 1, depositId: targetId)).Items.ShouldHaveSingleItem().BalanceTxnId.ShouldBeNull();
        var member = await GetRequiredService<CollectionBalanceAdminAppService>().GetMemberTxnsAsync(userId, 0, 1, txnId: txnId);
        member.Items.ShouldHaveSingleItem().DepositOrderId.ShouldBeNull();
        member.Items[0].TxHash.ShouldBeNull();

        var withdrawal = await withdrawals.GetWithdrawalsAsync(0, 1, withdrawalId: targetId);
        withdrawal.TotalCount.ShouldBe(1);
        withdrawal.Items.ShouldHaveSingleItem().Id.ShouldBe(targetId);
        withdrawal.Items[0].Status.ShouldBe(nameof(WithdrawStatus.Completed));

        (await deposits.GetDepositOrdersAsync(0, 1, depositId: Guid.NewGuid())).Items.ShouldBeEmpty();
        (await withdrawals.GetWithdrawalsAsync(0, 1, withdrawalId: Guid.NewGuid())).Items.ShouldBeEmpty();
    }
}
