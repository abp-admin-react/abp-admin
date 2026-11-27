using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Services;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Grids;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using Volo.Abp.Application.Dtos;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Volo.Abp.Validation;
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
            db.Tokens.Add(new ColToken(tokenId)
            {
                NetworkId = networkId, Symbol = "USDT", ContractAddress = "0x1234", MinDeposit = "1", Decimals = 18
            });
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

        var deposit = await deposits.GetDepositOrdersAsync(0, 1, "swept", "usdt", targetId, sweepId);
        deposit.TotalCount.ShouldBe(1);
        deposit.Items.ShouldHaveSingleItem().Id.ShouldBe(targetId);
        deposit.Items[0].BalanceTxnId.ShouldBe(txnId);
        deposit.Items[0].SweepTaskId.ShouldBe(sweepId);
        (await deposits.GetDepositOrdersAsync(0, 20, sweepTaskId: sweepId)).Items.ShouldHaveSingleItem().Id.ShouldBe(targetId);
        (await deposits.GetDepositOrdersAsync(0, 20, depositId: newerId, sweepTaskId: sweepId)).Items.ShouldBeEmpty();
        (await deposits.GetDepositOrdersAsync(0, 1, depositId: newerId)).Items.ShouldHaveSingleItem().BalanceTxnId.ShouldBeNull();
        (await deposits.GetSweepTasksAsync(0, 1, status: "swept", sweepTaskId: sweepId)).Items.ShouldHaveSingleItem().Id.ShouldBe(sweepId);
        (await deposits.GetSweepTasksAsync(0, 1, status: nameof(SweepTaskStatus.Queued), sweepTaskId: sweepId)).Items.ShouldBeEmpty();
        (await deposits.GetSweepTasksAsync(0, 1, sweepTaskId: Guid.NewGuid())).Items.ShouldBeEmpty();

        var grid = new GridQueryInput { MaxResultCount = 1 };
        var found = (await deposits.QueryDepositOrdersAsync(grid, "swept", "usdt", targetId, sweepId)).Items.ShouldHaveSingleItem();
        found.Id.ShouldBe(targetId);
        found.BalanceTxnId.ShouldBe(txnId);
        found.SweepTaskId.ShouldBe(sweepId);
        (await deposits.QueryDepositOrdersAsync(grid, depositId: newerId, sweepTaskId: sweepId)).Items.ShouldBeEmpty();
        (await deposits.QueryDepositOrdersAsync(grid, symbol: "usdc", depositId: targetId)).Items.ShouldBeEmpty();
        (await deposits.QueryDepositOrdersAsync(grid, status: nameof(DepositOrderStatus.PendingConfirm), depositId: targetId)).Items.ShouldBeEmpty();
        (await deposits.QuerySweepTasksAsync(grid, "swept", sweepId)).Items.ShouldHaveSingleItem().Id.ShouldBe(sweepId);
        (await deposits.QuerySweepTasksAsync(grid, nameof(SweepTaskStatus.Queued), sweepId)).Items.ShouldBeEmpty();

        // Even an exact persisted idempotency key cannot establish a link across account owners.
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await db.DepositOrders.Where(o => o.Id == targetId).ExecuteUpdateAsync(s => s.SetProperty(o => o.UserId, Guid.NewGuid()));
            await uow.CompleteAsync();
        }
        (await deposits.GetDepositOrdersAsync(0, 1, depositId: targetId)).Items.ShouldHaveSingleItem().BalanceTxnId.ShouldBeNull();
        (await deposits.QueryDepositOrdersAsync(grid, depositId: targetId)).Items.ShouldHaveSingleItem().BalanceTxnId.ShouldBeNull();
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

    [Fact]
    public async Task Deposit_Sweep_And_Vault_Get_And_Grid_Pages_Agree_When_Timestamps_Tie()
    {
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).Order().ToArray();
        var now = DateTime.UtcNow;
        await WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            for (var index = ids.Length - 1; index >= 0; index--)
            {
                var id = ids[index];
                var time = index == 0 ? now.AddDays(-1) : now;
                var amount = index + 1;
                db.DepositOrders.Add(new ColDepositOrder(id)
                {
                    TxHash = $"0x{id:N}", RawAmount = amount.ToString(), Amount = amount,
                    FromAddress = "0xa", ToAddress = "0xb", Status = DepositOrderStatus.Swept, CreationTime = time
                });
                db.SweepTasks.Add(new ColSweepTask(id)
                {
                    Address = "0xb", Amount = amount, Status = SweepTaskStatus.Swept, StatusChangedAt = time, CreationTime = time
                });
                db.VaultChecks.Add(new ColVaultCheck(id)
                {
                    Symbol = "USDT", RawVaultAmount = amount.ToString(), VaultBalance = amount, Difference = amount, CheckedAt = time
                });
            }
            await db.SaveChangesAsync();
        });

        var service = GetRequiredService<CollectionNetworkAdminAppService>();
        await AssertPagesAsync(skip => service.GetDepositOrdersAsync(skip, 2, status: ""),
            grid => service.QueryDepositOrdersAsync(grid, status: ""), row => row.Id, "amount");
        await AssertPagesAsync(skip => service.GetSweepTasksAsync(skip, 2, status: ""),
            grid => service.QuerySweepTasksAsync(grid, status: ""), row => row.Id, "amount");
        await AssertPagesAsync(skip => service.GetVaultChecksAsync(skip, 2), grid => service.QueryVaultChecksAsync(grid),
            row => row.Id, "difference");
        var historical = await service.QueryVaultChecksAsync(new GridQueryInput
        {
            Filters = [new() { Field = "status", Conditions = [new() { Op = "eq", Value = "历史记录" }] }]
        });
        historical.Items.Count.ShouldBe(4);
        historical.Items.ShouldAllBe(v => v.ReconciliationStatus == "Legacy" && v.ReferenceBlock == null
                                          && v.PendingCredit == null && v.PostedAfterBlock == null && v.ReconciledDifference == null);

        var missingLookups = new GridQueryInput
        {
            Keyword = "?",
            Filters = [new() { Field = "symbol", Conditions = [new() { Op = "eq", Value = "?" }] }]
        };
        var sweeps = await service.QuerySweepTasksAsync(missingLookups);
        sweeps.Items.Count.ShouldBe(4);
        sweeps.Items.ShouldAllBe(row => row.Symbol == "?");
        missingLookups.Filters.Add(new() { Field = "networkName", Conditions = [new() { Op = "eq", Value = "?" }] });
        var deposits = await service.QueryDepositOrdersAsync(missingLookups);
        deposits.Items.Count.ShouldBe(4);
        deposits.Items.ShouldAllBe(row => row.Symbol == "?" && row.NetworkName == "?");

        async Task AssertPagesAsync<T>(Func<int, Task<PagedResultDto<T>>> getPage,
            Func<GridQueryInput, Task<PagedResultDto<T>>> queryPage, Func<T, Guid> idOf, string sortField)
        {
            var expected = new[] { ids[1], ids[2], ids[3], ids[0] };
            for (var skip = 0; skip < expected.Length; skip += 2)
            {
                var get = await getPage(skip);
                var post = await queryPage(new GridQueryInput { SkipCount = skip, MaxResultCount = 2 });
                var page = expected.Skip(skip).Take(2).ToArray();
                get.TotalCount.ShouldBe(4);
                post.TotalCount.ShouldBe(4);
                get.Items.Select(idOf).ShouldBe(page);
                post.Items.Select(idOf).ShouldBe(page);
            }

            var sorted = await queryPage(new GridQueryInput { Sort = [new() { Field = sortField, Direction = "desc" }] });
            sorted.Items.Select(idOf).ShouldBe(ids.OrderDescending().ToArray());
        }
    }

    [Theory]
    [InlineData("unknown-state")]
    [InlineData("9999")]
    [InlineData("Swept,Swept")]
    public async Task Deposit_And_Sweep_Reject_Invalid_Status_On_Both_Read_Routes(string status)
    {
        var service = GetRequiredService<CollectionNetworkAdminAppService>();
        var grid = new GridQueryInput();
        await Should.ThrowAsync<AbpValidationException>(() => service.GetDepositOrdersAsync(0, 1, status));
        await Should.ThrowAsync<AbpValidationException>(() => service.QueryDepositOrdersAsync(grid, status));
        await Should.ThrowAsync<AbpValidationException>(() => service.GetSweepTasksAsync(0, 1, status));
        await Should.ThrowAsync<AbpValidationException>(() => service.QuerySweepTasksAsync(grid, status));
    }
}
