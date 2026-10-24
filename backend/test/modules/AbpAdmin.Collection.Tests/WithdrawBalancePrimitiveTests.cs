using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 提现冻结原语用例（提现 M1）：两桶搬运（Freeze/Unfreeze）、出账（Settle）、退款（Refund）
/// 的幂等与金额不足路径、并发冻结竞争、环境事务回滚、两桶审计不变量（BalanceAuditJob 口径）。
/// </summary>
public class WithdrawBalancePrimitiveTests : CollectionTestBase
{
    private static readonly Guid User1 = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private CollectionBalanceManager Manager =>
        GetRequiredService<CollectionBalanceManager>();

    [Fact]
    public async Task Freeze_Then_Unfreeze_Roundtrips_Both_Buckets()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var orderId = Guid.NewGuid();

        var freezeTxn = await Manager.FreezeAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
        (await BalanceOfAsync()).ShouldBe(70m);
        (await FrozenOfAsync()).ShouldBe(30m);

        // 重放同键同额：返回原流水，两桶不动
        var replay = await Manager.FreezeAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
        replay.ShouldBe(freezeTxn);
        (await BalanceOfAsync()).ShouldBe(70m);
        (await FrozenOfAsync()).ShouldBe(30m);

        await Manager.UnfreezeAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
        (await BalanceOfAsync()).ShouldBe(100m);
        (await FrozenOfAsync()).ShouldBe(0m);
    }

    [Fact]
    public async Task Freeze_Insufficient_Leaves_No_Orphan_Row()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 10m, $"dep-{Guid.NewGuid()}");
        var orderId = Guid.NewGuid();

        var ex = await Should.ThrowAsync<BusinessException>(
            () => Manager.FreezeAsync(User1, CollectionCurrency.Usdt, 20m, orderId));
        ex.Code.ShouldBe(CollectionErrorCodes.InsufficientBalance);

        // 失败不留孤儿流水（自开的事务性 UoW 整体回滚），两桶不动
        (await CountWithdrawTxnsAsync(orderId)).ShouldBe(0);
        (await BalanceOfAsync()).ShouldBe(10m);
        (await FrozenOfAsync()).ShouldBe(0m);
    }

    [Fact]
    public async Task Freeze_Same_Order_Different_Amount_Conflicts()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var orderId = Guid.NewGuid();
        await Manager.FreezeAsync(User1, CollectionCurrency.Usdt, 30m, orderId);

        // 同键异额：状态机错乱信号，不是幂等重放
        var ex = await Should.ThrowAsync<BusinessException>(
            () => Manager.FreezeAsync(User1, CollectionCurrency.Usdt, 40m, orderId));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawActionConflict);
        (await BalanceOfAsync()).ShouldBe(70m);
    }

    [Fact]
    public async Task Settle_Keeps_Available_And_Cleans_Frozen_Idempotently()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var orderId = Guid.NewGuid();
        await Manager.FreezeAsync(User1, CollectionCurrency.Usdt, 30m, orderId);

        var settleTxn = await Manager.DeductFrozenAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
        (await BalanceOfAsync()).ShouldBe(70m);
        (await FrozenOfAsync()).ShouldBe(0m);

        var replay = await Manager.DeductFrozenAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
        replay.ShouldBe(settleTxn);
        (await BalanceOfAsync()).ShouldBe(70m);
        (await FrozenOfAsync()).ShouldBe(0m);
    }

    [Fact]
    public async Task Refund_After_Settle_Restores_Available()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var orderId = Guid.NewGuid();
        await Manager.FreezeAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
        await Manager.DeductFrozenAsync(User1, CollectionCurrency.Usdt, 30m, orderId);

        var refundTxn = await Manager.RefundAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
        (await BalanceOfAsync()).ShouldBe(100m);
        (await FrozenOfAsync()).ShouldBe(0m);

        var replay = await Manager.RefundAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
        replay.ShouldBe(refundTxn);
        (await BalanceOfAsync()).ShouldBe(100m);
    }

    [Fact]
    public async Task Unfreeze_Without_Frozen_Throws_State_Conflict_No_Orphan()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var orderId = Guid.NewGuid();

        // 两桶状态与流水不符（没冻结过就想解冻）：完整性异常，且不留孤儿流水
        var ex = await Should.ThrowAsync<BusinessException>(
            () => Manager.UnfreezeAsync(User1, CollectionCurrency.Usdt, 10m, orderId));
        ex.Code.ShouldBe(CollectionErrorCodes.BalanceStateConflict);
        (await CountWithdrawTxnsAsync(orderId)).ShouldBe(0);
        (await BalanceOfAsync()).ShouldBe(100m);
    }

    [Fact]
    public async Task Concurrent_Freezes_Only_One_Wins()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var orderA = Guid.NewGuid();
        var orderB = Guid.NewGuid();

        // 两个独立 scope 并发冻结各 60：行锁串行化，先到者赢，后来者可用不足
        var tasks = Task.WhenAll(
            FreezeInNewScopeAsync(orderA, 60m),
            FreezeInNewScopeAsync(orderB, 60m));
        var ex = await Should.ThrowAsync<BusinessException>(() => tasks);
        ex.Code.ShouldBe(CollectionErrorCodes.InsufficientBalance);

        (await BalanceOfAsync()).ShouldBe(40m);
        (await FrozenOfAsync()).ShouldBe(60m);
        (await CountWithdrawTxnsAsync(orderA) + await CountWithdrawTxnsAsync(orderB)).ShouldBe(1);
    }

    [Fact]
    public async Task Freeze_Joins_Ambient_Transaction_And_Rolls_Back_With_It()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var orderId = Guid.NewGuid();

        // 生产形态（提现提交编排的事务）：撤回整个 UoW 时冻结流水与两桶变化一起消失
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        using (var uow = uowManager.Begin(requiresNew: true, isTransactional: true))
        {
            await Manager.FreezeAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
            await uow.RollbackAsync();
        }

        (await BalanceOfAsync()).ShouldBe(100m);
        (await FrozenOfAsync()).ShouldBe(0m);
        (await CountWithdrawTxnsAsync(orderId)).ShouldBe(0);
    }

    [Fact]
    public async Task TwoBucket_Audit_Invariant_Holds_Across_Lifecycle_And_Catches_Corruption()
    {
        var handler = GetRequiredService<AbpAdmin.Collection.Jobs.BalanceAuditJobHandler>();

        // 全生命周期：充值 → 冻结 → 出账 → 退款，两桶口径始终平
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var orderId = Guid.NewGuid();
        await Manager.FreezeAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
        (await handler.QueryMismatchesAsync()).ShouldBeEmpty();

        await Manager.DeductFrozenAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
        (await handler.QueryMismatchesAsync()).ShouldBeEmpty();

        await Manager.RefundAsync(User1, CollectionCurrency.Usdt, 30m, orderId);
        (await handler.QueryMismatchesAsync()).ShouldBeEmpty();

        // 人为做坏冻结桶（绕过管理器直接改行）：两桶口径必须点名（可用侧数值仍正确）
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await dbContext.Balances
                .Where(b => b.UserId == User1 && b.Currency == CollectionCurrency.Usdt)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Frozen, 5m));
            await uow.CompleteAsync();
        }

        var mismatches = await handler.QueryMismatchesAsync();
        var row = mismatches.Single(r => r.UserId == User1 && r.Currency == (int)CollectionCurrency.Usdt);
        row.SumFrozen.ShouldBe(0m);
        row.Frozen.ShouldBe(5m);
        row.SumTxns.ShouldBe(row.Balance); // 可用侧不误报
    }

    // ── 共用 ─────────────────────────────────────────────────────────────────────────────

    private async Task<Guid> FreezeInNewScopeAsync(Guid orderId, decimal amount)
    {
        using var scope = ServiceProvider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<CollectionBalanceManager>();
        return await manager.FreezeAsync(User1, CollectionCurrency.Usdt, amount, orderId);
    }

    private async Task<decimal> BalanceOfAsync()
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var row = await dbContext.Balances.AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == User1 && b.Currency == CollectionCurrency.Usdt);
            return row?.Balance ?? 0m;
        });
    }

    private async Task<decimal> FrozenOfAsync()
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var row = await dbContext.Balances.AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == User1 && b.Currency == CollectionCurrency.Usdt);
            return row?.Frozen ?? 0m;
        });
    }

    private async Task<int> CountWithdrawTxnsAsync(Guid orderId)
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.BalanceTxns.AsNoTracking()
                .CountAsync(t => t.BusinessId == orderId && t.BusinessType == CollectionBusinessTypes.Withdraw);
        });
    }

    private async Task<T> ReadAsync<T>(Func<Task<T>> read)
    {
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        if (uowManager.Current != null)
        {
            return await read();
        }

        using var uow = uowManager.Begin();
        var result = await read();
        await uow.CompleteAsync();
        return result;
    }
}
