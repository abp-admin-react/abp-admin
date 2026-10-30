using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 余额记账用例（计划第 3.1、5.5、8 节 M2 验收清单）：扣款、回冲带 ReversedAt IS NULL、
/// 并发两次回冲只加回一次余额、无未回冲扣款时回冲成功、双币、流水合计作业、GetDebitAsync、
/// 同单幂等与冲突、回冲后可再扣、入账幂等、清单游标与 7 天重扫。
/// </summary>
public class CollectionBalanceManagerTests : CollectionTestBase
{
    private static readonly Guid User1 = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid User2 = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private CollectionBalanceManager Manager =>
        GetRequiredService<CollectionBalanceManager>();

    [Fact]
    public async Task Debit_Then_Reverse_Restores_Balance_And_Allows_New_Debit()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var businessId = Guid.NewGuid();

        var txn = await Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, 30m);
        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(70m);

        // GetDebitAsync：未回冲扣款可见（补状态作业的判据）
        var debit = await Manager.FindUnreversedDebitAsync("Order", businessId);
        debit.ShouldNotBeNull();
        debit.Id.ShouldBe(txn);
        debit.Amount.ShouldBe(-30m);
        debit.BalanceAfter.ShouldBe(70m);

        await Manager.ReverseAsync("Order", businessId);
        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(100m);
        (await Manager.FindUnreversedDebitAsync("Order", businessId)).ShouldBeNull();

        // 回冲后再次扣款：部分唯一索引已释放，新流水用新键（计划第 5.5 节）
        var txn2 = await Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, 30m);
        txn2.ShouldNotBe(txn);
        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(70m);

        // 账本不变量：流水之和 == 余额行
        (await SumTxnsAsync(User1, CollectionCurrency.Usdt)).ShouldBe(70m);
    }

    [Fact]
    public async Task Insufficient_Balance_Leaves_No_Orphan_Txn_Row()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 10m, $"dep-{Guid.NewGuid()}");
        var businessId = Guid.NewGuid();

        var ex = await Should.ThrowAsync<BusinessException>(
            () => Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, 20m));
        ex.Code.ShouldBe(CollectionErrorCodes.InsufficientBalance);

        // 失败不留孤儿流水（扣款在自开的事务性 UoW 里整体回滚），余额不动
        (await CountTxnsAsync(businessId)).ShouldBe(0);
        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(10m);
    }

    [Fact]
    public async Task Same_Business_Debit_Is_Idempotent_And_Conflict_When_Different()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var businessId = Guid.NewGuid();

        var first = await Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, 30m);
        var retry = await Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, 30m);
        retry.ShouldBe(first); // 同金额同币种：返回原流水，不重复扣
        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(70m);

        var ex = await Should.ThrowAsync<BusinessException>(
            () => Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, 40m));
        ex.Code.ShouldBe(CollectionErrorCodes.DebitConflict);
        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(70m);
    }

    [Fact]
    public async Task Reverse_Is_Idempotent_And_NoOp_Without_Debit()
    {
        // 没有未回冲扣款时回冲直接成功（计划验收清单）
        await Manager.ReverseAsync("Order", Guid.NewGuid());

        await Manager.CreditAsync(User1, CollectionCurrency.Usdc, 50m, $"dep-{Guid.NewGuid()}");
        var businessId = Guid.NewGuid();
        await Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdc, 20m);

        await Manager.ReverseAsync("Order", businessId);
        await Manager.ReverseAsync("Order", businessId); // 重复回冲只生效一次
        (await BalanceOfAsync(User1, CollectionCurrency.Usdc)).ShouldBe(50m);

        // 只有一条回冲流水
        var reverses = await CountReversesAsync(businessId);
        reverses.ShouldBe(1);
    }

    [Fact]
    public async Task Concurrent_Reverse_Credits_Back_Only_Once()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var businessId = Guid.NewGuid();
        await Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, 40m);

        // 并发两次回冲（各自独立 scope 的 UoW，真 PG 行为）：余额只加回一次
        await Task.WhenAll(
            ReverseInNewScopeAsync(businessId),
            ReverseInNewScopeAsync(businessId));

        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(100m);
        (await CountReversesAsync(businessId)).ShouldBe(1);
    }

    [Fact]
    public async Task Dual_Currency_Balances_Are_Independent()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 10m, $"dep-u-{Guid.NewGuid()}");
        await Manager.CreditAsync(User1, CollectionCurrency.Usdc, 20m, $"dep-c-{Guid.NewGuid()}");
        var businessId = Guid.NewGuid();

        // USDT 余额不够扣 USDC 的事（币种隔离；扣 USDC 只看 USDC 余额）
        var ex = await Should.ThrowAsync<BusinessException>(
            () => Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdc, 25m));
        ex.Code.ShouldBe(CollectionErrorCodes.InsufficientBalance);

        var ok = await Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, 10m);
        ok.ShouldNotBe(Guid.Empty);
        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(0m);
        (await BalanceOfAsync(User1, CollectionCurrency.Usdc)).ShouldBe(20m);

        // 法币/其他币种直接拒绝（只允许稳定币）
        var fiat = await Should.ThrowAsync<BusinessException>(
            () => Manager.DebitAsync("Order", Guid.NewGuid(), User1, CollectionCurrency.Cny, 1m));
        fiat.Code.ShouldBe(CollectionErrorCodes.InvalidAmount);
    }

    [Fact]
    public async Task Credit_Is_Idempotent_By_Deposit_Id()
    {
        var key = $"dep-{Guid.NewGuid()}";

        var first = await Manager.CreditAsync(User2, CollectionCurrency.Usdt, 12.5m, key);
        var again = await Manager.CreditAsync(User2, CollectionCurrency.Usdt, 12.5m, key);
        again.ShouldBe(first);
        (await BalanceOfAsync(User2, CollectionCurrency.Usdt)).ShouldBe(12.5m); // 只入账一次
    }

    [Fact]
    public async Task ListCredited_Advances_Cursor_And_Rescans_Recent_Skipped()
    {
        var a = await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 1m, $"dep-{Guid.NewGuid()}");
        var b = await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 2m, $"dep-{Guid.NewGuid()}");

        // 正向拉取：批 1
        var batch1 = await Manager.ListCreditedAsync(0, 1);
        batch1.Items.Count.ShouldBe(1);
        batch1.Cursor.ShouldBe(SeqOf(a));

        // 游标之后：正向段带出 b；同时 7 天重扫会把窗口内、序号在游标之前的近期行
        //（比如曾被跳过的 a）再带回——去重由补插作业按充值订单存在性承担，清单只负责不漏
        var batch2 = await Manager.ListCreditedAsync(batch1.Cursor, 10);
        batch2.Items.Select(i => i.BalanceTxnId).ShouldContain(b);
        batch2.Items.Select(i => i.BalanceTxnId).ShouldContain(a);
        var cursor2 = batch2.Cursor;
        cursor2.ShouldBe(SeqOf(b));

        // 7 天重扫：序号落在游标之前、但 CreationTime 在窗口内的行会被再带回
        //（「被跳过的行只靠 7 天重扫补」——进款单补插失败后的恢复路径）
        var rescan = await Manager.ListCreditedAsync(cursor2, 10);
        rescan.Items.Select(i => i.BalanceTxnId).ShouldContain(a);
        rescan.Items.Select(i => i.BalanceTxnId).ShouldContain(b);

        // 扣款流水不出现在充值清单里
        await Manager.DebitAsync("Order", Guid.NewGuid(), User1, CollectionCurrency.Usdt, 0.5m);
        var after = await Manager.ListCreditedAsync(0, 100);
        after.Items.Count(i => i.BalanceTxnId == a || i.BalanceTxnId == b).ShouldBe(2);
    }


    [Fact]
    public async Task Debit_Joins_Ambient_Transaction_And_Rolls_Back_With_It()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var businessId = Guid.NewGuid();

        // 生产分支（订单 PayWithBalanceAsync 的形态）：调用方开事务性 requiresNew，扣款加入其中；
        // 调用方不提交而回滚时，扣款流水与余额变化必须一起消失——「扣款 + 标支付同事务」的前提
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        using (var uow = uowManager.Begin(requiresNew: true, isTransactional: true))
        {
            await Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, 30m);
            await uow.RollbackAsync();
        }

        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(100m);
        (await CountTxnsAsync(businessId)).ShouldBe(0);

        // 提交路径：同形态下 Complete 后行落库
        using (var uow = uowManager.Begin(requiresNew: true, isTransactional: true))
        {
            await Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, 30m);
            await uow.CompleteAsync();
        }

        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(70m);
        (await CountTxnsAsync(businessId)).ShouldBe(1);
    }

    [Fact]
    public async Task Debit_Rejects_NonTransactional_Ambient_Uow()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 10m, $"dep-{Guid.NewGuid()}");
        var businessId = Guid.NewGuid();

        // 非事务环境 UoW 下各语句自动提交，失败路径会留下孤儿扣款行（补状态作业会把它当
        // 付款证据标 Paid）——必须当场拒绝，不降级（fail-closed）
        using var ambient = GetRequiredService<IUnitOfWorkManager>().Begin(isTransactional: false);
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => Manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, 20m));
        ex.Message.ShouldContain("transactional");

        (await CountTxnsAsync(businessId)).ShouldBe(0);
        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(10m);
        await ambient.CompleteAsync();
    }

    [Fact]
    public async Task Concurrent_Same_Business_Debit_Returns_One_Txn_And_Debits_Once()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 100m, $"dep-{Guid.NewGuid()}");
        var businessId = Guid.NewGuid();

        // 两个独立 scope 并发扣同一业务单：部分唯一索引 + ON CONFLICT 保证只有一个赢家，
        // 输家走幂等分支拿回原流水号——余额只扣一次
        var results = await Task.WhenAll(
            DebitInNewScopeAsync(businessId, 30m),
            DebitInNewScopeAsync(businessId, 30m));

        results[0].ShouldBe(results[1]);
        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(70m);
        var rows = await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.BalanceTxns.AsNoTracking()
                .CountAsync(t => t.BusinessId == businessId && t.Action == BalanceActions.BusinessDebit);
        });
        rows.ShouldBe(1);
    }

    [Fact]
    public async Task ListCredited_Does_Not_Rescan_Rows_Outside_7Day_Window()
    {
        var a = await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 1m, $"dep-{Guid.NewGuid()}");
        var b = await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 2m, $"dep-{Guid.NewGuid()}");

        // 把 a 回拨到 8 天前：游标超过它之后，重扫不得再带回（无界重扫防线）
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await dbContext.BalanceTxns
                .Where(t => t.Id == a)
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.CreationTime, t => t.CreationTime.AddDays(-8)));
            await uow.CompleteAsync();
        }

        var batch = await Manager.ListCreditedAsync(SeqOf(b), 10);
        batch.Items.Select(i => i.BalanceTxnId).ShouldContain(b);
        batch.Items.Select(i => i.BalanceTxnId).ShouldNotContain(a);
    }

    [Fact]
    public async Task Balance_Audit_Job_Runs_Without_Exception_Healthy_Or_Corrupted()
    {
        await Manager.CreditAsync(User1, CollectionCurrency.Usdt, 5m, $"dep-{Guid.NewGuid()}");
        // 具体类型直解析（类代理兼容虚方法，QueryMismatchesAsync 可直调）；
        // 接口枚举解析仍是宿主挑作业的方式
        var handler = GetRequiredService<AbpAdmin.Collection.Jobs.BalanceAuditJobHandler>();

        // 健康账本：检测谓词必须为空（只测不抛时恒空退化也绿）
        (await handler.QueryMismatchesAsync()).ShouldBeEmpty();
        await handler.ExecuteAsync(new AbpAdmin.ScheduledJobs.ScheduledJobContext
        {
            ScheduledJobId = Guid.NewGuid(),
            JobType = AbpAdmin.ScheduledJobs.ScheduledJobConsts.BalanceAuditJobType,
        });

        // 人为做坏账本（绕过管理器直接改余额行）：作业仍然只读不写、不抛
        using (var uow = GetRequiredService<IUnitOfWorkManager>().Begin())
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            await dbContext.Balances
                .Where(b => b.UserId == User1 && b.Currency == CollectionCurrency.Usdt)
                .ExecuteUpdateAsync(s => s.SetProperty(b => b.Balance, 999m));
            await uow.CompleteAsync();
        }

        await handler.ExecuteAsync(new AbpAdmin.ScheduledJobs.ScheduledJobContext
        {
            ScheduledJobId = Guid.NewGuid(),
            JobType = AbpAdmin.ScheduledJobs.ScheduledJobConsts.BalanceAuditJobType,
        });

        (await SumTxnsAsync(User1, CollectionCurrency.Usdt)).ShouldBe(5m); // 流水侧未被作业改动
        (await BalanceOfAsync(User1, CollectionCurrency.Usdt)).ShouldBe(999m); // 余额侧同样只读——审计不许自愈

        // 检测谓词点名坏行：用户/币种/两侧数值齐全（告警日志的忠实性由它保证）
        var mismatches = await handler.QueryMismatchesAsync();
        var row = mismatches.Single(r => r.UserId == User1 && r.Currency == (int)CollectionCurrency.Usdt);
        row.SumTxns.ShouldBe(5m);
        row.Balance.ShouldBe(999m);
        row.OnlyOnBalanceSide.ShouldBeFalse();
    }

    // ── 共用 ─────────────────────────────────────────────────────────────────────────────

    private async Task<Guid> DebitInNewScopeAsync(Guid businessId, decimal amount)
    {
        using var scope = ServiceProvider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<CollectionBalanceManager>();
        return await manager.DebitAsync("Order", businessId, User1, CollectionCurrency.Usdt, amount);
    }

    private async Task ReverseInNewScopeAsync(Guid businessId)
    {
        using var scope = ServiceProvider.CreateScope();
        // 独立 scope = 独立 UoW（并发用多个 scope，计划第 8 节）
        var manager = scope.ServiceProvider.GetRequiredService<CollectionBalanceManager>();
        await manager.ReverseAsync("Order", businessId);
    }

    private async Task<decimal> BalanceOfAsync(Guid userId, CollectionCurrency currency)
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var row = await dbContext.Balances.AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Currency == currency);
            return row?.Balance ?? 0m;
        });
    }

    private async Task<decimal> SumTxnsAsync(Guid userId, CollectionCurrency currency)
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.BalanceTxns.AsNoTracking()
                .Where(t => t.UserId == userId && t.Currency == currency)
                .SumAsync(t => t.Amount);
        });
    }

    private async Task<int> CountTxnsAsync(Guid businessId)
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.BalanceTxns.AsNoTracking()
                .CountAsync(t => t.BusinessId == businessId);
        });
    }

    private async Task<int> CountReversesAsync(Guid businessId)
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.BalanceTxns.AsNoTracking()
                .CountAsync(t => t.BusinessId == businessId && t.Action == BalanceActions.DebitReverse);
        });
    }

    private long SeqOf(Guid txnId)
    {
        return ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.BalanceTxns.AsNoTracking().Where(t => t.Id == txnId).Select(t => t.Seq).FirstAsync();
        }).GetAwaiter().GetResult();
    }

    /// <summary>读取助手：没有环境工作单元时自开一个（同集成服务的 ReadAsync 纪律）。</summary>
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
