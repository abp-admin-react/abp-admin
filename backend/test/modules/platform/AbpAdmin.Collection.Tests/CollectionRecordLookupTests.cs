using System;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
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

        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            InsertOrders(targetId, now.AddDays(-1), WithdrawStatus.Completed);
            InsertOrders(newerId, now, WithdrawStatus.Pending);
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

        var withdrawal = await withdrawals.GetWithdrawalsAsync(0, 1, withdrawalId: targetId);
        withdrawal.TotalCount.ShouldBe(1);
        withdrawal.Items.ShouldHaveSingleItem().Id.ShouldBe(targetId);
        withdrawal.Items[0].Status.ShouldBe(nameof(WithdrawStatus.Completed));

        (await deposits.GetDepositOrdersAsync(0, 1, depositId: Guid.NewGuid())).Items.ShouldBeEmpty();
        (await withdrawals.GetWithdrawalsAsync(0, 1, withdrawalId: Guid.NewGuid())).Items.ShouldBeEmpty();
    }
}
