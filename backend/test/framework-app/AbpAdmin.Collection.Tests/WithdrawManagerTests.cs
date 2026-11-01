using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Withdrawals;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Ledger.Integration.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;
using Xunit;

namespace AbpAdmin.Collection;

/// <summary>
/// 提现状态机与账本过账用例（提现 M2）：提交冻结/校验矩阵/当日额度（含并发竞争）/
/// 撤单/审核/拒绝/失败退款的 CAS 迁移、两桶与账本 2201/2202/2241/6003 的全程一致性、
/// 账本过账幂等重放。账本侧余额断言走 ILedgerIntegrationService.GetBalanceAsync。
/// </summary>
public class WithdrawManagerTests : CollectionTestBase
{
    private static readonly Guid User1 = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid User2 = Guid.Parse("88888888-8888-8888-8888-888888888888");
    private static readonly Guid Admin = Guid.Parse("99999999-9999-9999-9999-999999999999");

    private WithdrawManager Manager => GetRequiredService<WithdrawManager>();
    private CollectionBalanceManager Balances => GetRequiredService<CollectionBalanceManager>();
    private CollectionLedgerPoster Poster => GetRequiredService<CollectionLedgerPoster>();
    private ILedgerIntegrationService Ledger => GetRequiredService<ILedgerIntegrationService>();

    private async Task FundAsync(Guid userId, decimal amount, CollectionCurrency currency = CollectionCurrency.Usdt)
    {
        // 生产口径：余额行入账 + 账本开账（上线回填路径 PostOpeningAsync）
        await Balances.CreditAsync(userId, currency, amount, $"dep-{Guid.NewGuid()}");
        await Poster.PostOpeningAsync(userId, currency, amount);
    }

    [Fact]
    public async Task Submit_Freezes_Both_Books_And_Creates_Pending_Order()
    {
        await FundAsync(User1, 1000m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt,
            "0x1111111111111111111111111111111111111111", 100m);

        order.Status.ShouldBe(WithdrawStatus.Pending);
        order.Fee.ShouldBe(0.4m);          // 100 × 0.004，8 位截断
        order.ActualAmount.ShouldBe(99.6m);
        order.FeeRate.ShouldBe(0.004m);

        // 两桶：可用 900 / 冻结 100
        (await BalanceOfAsync(User1)).ShouldBe(900m);
        (await FrozenOfAsync(User1)).ShouldBe(100m);

        // 账本同事务：2201 = 900、2202 = 100（opening 1000 − freeze 100）
        (await UserAvailableAsync(User1)).ShouldBe(900m);
        (await UserFrozenAsync(User1)).ShouldBe(100m);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("0x123")]
    [InlineData("0x0000000000000000000000000000000000000000")] // 黑洞地址
    public async Task Submit_Rejects_Invalid_Addresses(string address)
    {
        await FundAsync(User1, 1000m);
        var ex = await Should.ThrowAsync<BusinessException>(
            () => Manager.SubmitAsync(User1, CollectionCurrency.Usdt, address, 100m));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawInvalidAddress);
    }

    [Fact]
    public async Task Submit_Below_Min_Rejected()
    {
        await FundAsync(User1, 1000m);
        var ex = await Should.ThrowAsync<BusinessException>(
            () => Manager.SubmitAsync(User1, CollectionCurrency.Usdt,
                "0x1111111111111111111111111111111111111111", 5m));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawBelowMin);
    }

    [Fact]
    public async Task Submit_Blocked_By_Halt_Switch()
    {
        await FundAsync(User1, 1000m);
        await GetRequiredService<ISettingManager>()
            .SetGlobalAsync(AbpAdmin.Collection.Settings.CollectionSettings.Withdraw.Halt, "true");
        try
        {
            var ex = await Should.ThrowAsync<BusinessException>(
                () => Manager.SubmitAsync(User1, CollectionCurrency.Usdt,
                    "0x1111111111111111111111111111111111111111", 100m));
            ex.Code.ShouldBe(CollectionErrorCodes.WithdrawHalted);
        }
        finally
        {
            await GetRequiredService<ISettingManager>()
                .SetGlobalAsync(AbpAdmin.Collection.Settings.CollectionSettings.Withdraw.Halt, "false");
        }
    }

    [Fact]
    public async Task Daily_Count_Quota_And_Cancel_Releases_It()
    {
        await SetSettingAsync(AbpAdmin.Collection.Settings.CollectionSettings.Withdraw.MaxDailyCount, "1");
        await FundAsync(User1, 1000m);
        var address = "0x1111111111111111111111111111111111111111";

        var first = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt, address, 100m);
        var ex = await Should.ThrowAsync<BusinessException>(
            () => Manager.SubmitAsync(User1, CollectionCurrency.Usdt, address, 100m));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawDailyCountExceeded);

        // 撤单释放额度；余额/账本回滚到冻结前
        await Manager.CancelAsync(User1, first.Id);
        (await BalanceOfAsync(User1)).ShouldBe(1000m);
        (await UserAvailableAsync(User1)).ShouldBe(1000m);

        var again = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt, address, 100m);
        again.Status.ShouldBe(WithdrawStatus.Pending);
    }

    [Fact]
    public async Task Concurrent_Cross_Currency_Submits_Do_Not_Pierce_Daily_Count()
    {
        // 笔数按用户跨币种合并（线上按用户不按网络）；并发异币种提交曾可同时读到旧计数双双穿透——
        // 现在提交锁住 USDT+USDC 两行余额再计数（Cursor 计划：对齐线上「先锁资金账户再数笔数」）
        await SetSettingAsync(AbpAdmin.Collection.Settings.CollectionSettings.Withdraw.MaxDailyCount, "1");
        await Balances.CreditAsync(User1, CollectionCurrency.Usdt, 1000m, $"dep-a-{Guid.NewGuid()}");
        await Poster.PostOpeningAsync(User1, CollectionCurrency.Usdt, 1000m);
        await Balances.CreditAsync(User1, CollectionCurrency.Usdc, 1000m, $"dep-b-{Guid.NewGuid()}");
        await Poster.PostOpeningAsync(User1, CollectionCurrency.Usdc, 1000m);

        var tasks = Task.WhenAll(
            SubmitInNewScopeAsync(User1, CollectionCurrency.Usdt, 100m),
            SubmitInNewScopeAsync(User1, CollectionCurrency.Usdc, 100m));
        var ex = await Should.ThrowAsync<BusinessException>(() => tasks);
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawDailyCountExceeded);

        (await CountOrdersAsync(User1)).ShouldBe(1); // 只有一笔立住，另一笔被笔数挡下
    }

    [Fact]
    public async Task Cancel_Only_Own_Pending_Order()
    {
        await FundAsync(User1, 1000m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt,
            "0x1111111111111111111111111111111111111111", 100m);

        var notOwner = await Should.ThrowAsync<BusinessException>(
            () => Manager.CancelAsync(User2, order.Id));
        notOwner.Code.ShouldBe(CollectionErrorCodes.WithdrawNotOwner);

        await Manager.ApproveAsync(order.Id, Admin, null);
        var tooLate = await Should.ThrowAsync<BusinessException>(
            () => Manager.CancelAsync(User1, order.Id));
        tooLate.Code.ShouldBe(CollectionErrorCodes.WithdrawInvalidStatus);
    }

    [Fact]
    public async Task Approve_Settles_Frozen_And_Posts_Payable_And_Fee()
    {
        await FundAsync(User1, 1000m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt,
            "0x1111111111111111111111111111111111111111", 100m);

        await Manager.ApproveAsync(order.Id, Admin, "初审通过");

        var stored = await GetOrderAsync(order.Id);
        stored.Status.ShouldBe(WithdrawStatus.Processing);
        stored.AuditUserId.ShouldBe(Admin);
        stored.AuditTime.ShouldNotBeNull();

        // 两桶：可用 900 不动、冻结清零
        (await BalanceOfAsync(User1)).ShouldBe(900m);
        (await FrozenOfAsync(User1)).ShouldBe(0m);

        // 账本：2202 清零、应付 +99.6、手续费收入 +0.4
        (await UserFrozenAsync(User1)).ShouldBe(0m);
        (await PlatformBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(99.6m);
        (await PlatformBalanceAsync(FinanceAccountCodes.WithdrawFeeIncome)).ShouldBe(0.4m);

        // 双审拒绝（CAS）
        var second = await Should.ThrowAsync<BusinessException>(
            () => Manager.ApproveAsync(order.Id, Admin, null));
        second.Code.ShouldBe(CollectionErrorCodes.WithdrawInvalidStatus);
    }

    [Fact]
    public async Task Reject_Requires_Remark_And_Unfreezes()
    {
        await FundAsync(User1, 1000m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt,
            "0x1111111111111111111111111111111111111111", 100m);

        var noRemark = await Should.ThrowAsync<BusinessException>(
            () => Manager.RejectAsync(order.Id, Admin, " "));
        noRemark.Code.ShouldBe(CollectionErrorCodes.WithdrawRemarkRequired);

        await Manager.RejectAsync(order.Id, Admin, "风控拒绝");
        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Rejected);
        (await BalanceOfAsync(User1)).ShouldBe(1000m);
        (await FrozenOfAsync(User1)).ShouldBe(0m);
        (await UserAvailableAsync(User1)).ShouldBe(1000m);
        (await UserFrozenAsync(User1)).ShouldBe(0m);
    }

    [Fact]
    public async Task Refund_After_Approve_Restores_Available_And_Fee()
    {
        await FundAsync(User1, 1000m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt,
            "0x1111111111111111111111111111111111111111", 100m);
        await Manager.ApproveAsync(order.Id, Admin, null);

        // 广播失败态（M3 worker 的迁移；测试用受跟踪实体直接置态）
        await ForceStatusAsync(order.Id, WithdrawStatus.BroadcastFailed, "广播失败");

        await Manager.RefundAfterApproveAsync(order.Id, Admin, "链上未付出，退款");
        (await GetOrderAsync(order.Id)).Status.ShouldBe(WithdrawStatus.Rejected);

        // 净额 + 手续费全部退回可用；账本 2241/6003 清零
        (await BalanceOfAsync(User1)).ShouldBe(1000m);
        (await FrozenOfAsync(User1)).ShouldBe(0m);
        (await UserAvailableAsync(User1)).ShouldBe(1000m);
        (await PlatformBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(0m);
        (await PlatformBalanceAsync(FinanceAccountCodes.WithdrawFeeIncome)).ShouldBe(0m);
    }

    [Fact]
    public async Task Ledger_Postings_Are_Idempotent_On_Replay()
    {
        await FundAsync(User1, 1000m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt,
            "0x1111111111111111111111111111111111111111", 100m);

        // 同一事件重放：引擎按 (BizType, BizRefId, Phase) 返回原凭证
        var v1 = await Poster.PostFreezeAsync(order);
        var v2 = await Poster.PostFreezeAsync(order);
        v1.ShouldBe(v2);
        (await UserFrozenAsync(User1)).ShouldBe(100m);
    }

    [Fact]
    public async Task Full_Lifecycle_Keeps_Buckets_And_Ledger_In_Sync()
    {
        await FundAsync(User1, 1000m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt,
            "0x1111111111111111111111111111111111111111", 100m);
        await Manager.ApproveAsync(order.Id, Admin, null);

        // 打款完成（M3 worker 调；此处直接过账 payout 验证闭环）
        await Poster.PostPayoutAsync(await GetOrderAsync(order.Id), "0xabc");

        // 终态：可用 900（100 已提走）、账本 2201 同步；链上资产 1000 - 99.6 = 900.4
        (await BalanceOfAsync(User1)).ShouldBe(900m);
        (await UserAvailableAsync(User1)).ShouldBe(900m);
        (await PlatformBalanceAsync(FinanceAccountCodes.OnChainAsset)).ShouldBe(900.4m);
        (await PlatformBalanceAsync(FinanceAccountCodes.AccountsPayable)).ShouldBe(0m);
    }

    [Fact]
    public async Task Per_Order_Amount_Limit_Matches_Online_Semantics()
    {
        await SetSettingAsync(AbpAdmin.Collection.Settings.CollectionSettings.Withdraw.MaxDailyAmount, "150");
        await FundAsync(User1, 1000m);
        var address = "0x1111111111111111111111111111111111111111";

        // 单笔上限口径（线上 MaxDailyWithdraw 同语义）：本笔 > 150 拒绝；两笔各自 ≤ 150、
        // 合计 > 150 仍可过——没有当日金额池（Cursor 计划核实修正：原加总口径作废）
        var ex = await Should.ThrowAsync<BusinessException>(
            () => Manager.SubmitAsync(User1, CollectionCurrency.Usdt, address, 150.5m));
        ex.Code.ShouldBe(CollectionErrorCodes.WithdrawDailyAmountExceeded);

        var first = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt, address, 100m);
        var second = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt, address, 50m);
        first.Status.ShouldBe(WithdrawStatus.Pending);
        second.Status.ShouldBe(WithdrawStatus.Pending);
    }

    [Fact]
    public async Task Per_Order_Limit_Zero_Or_Empty_Means_Unlimited()
    {
        await FundAsync(User1, 10000m);
        var address = "0x1111111111111111111111111111111111111111";

        // 空/0 = 不限：先把笔数放开以隔离金额断言（默认 MaxDailyCount=3 会先拦）
        await SetSettingAsync(AbpAdmin.Collection.Settings.CollectionSettings.Withdraw.MaxDailyCount, "99");
        var big = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt, address, 5000m);
        big.Status.ShouldBe(WithdrawStatus.Pending);

        await SetSettingAsync(AbpAdmin.Collection.Settings.CollectionSettings.Withdraw.MaxDailyAmount, "0");
        var another = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt, address, 3000m);
        another.Status.ShouldBe(WithdrawStatus.Pending);
    }

    [Fact]
    public async Task Submit_Amount_Truncated_To_8dp_Before_Limits()
    {
        await FundAsync(User1, 1000m);
        // 18 位毛额先向零截 8 位（线上 RoundUsdt8）：100.123456789123 → 100.12345678，
        // 手续费按截断后金额计（0.004 × 100.12345678 = 0.400493827…截 8 位 → 0.40049382）
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt,
            "0x1111111111111111111111111111111111111111", 100.123456789123m);
        order.Amount.ShouldBe(100.12345678m);
        order.Fee.ShouldBe(0.40049382m);
        order.ActualAmount.ShouldBe(99.72296296m);
    }

    [Fact]
    public async Task Zero_Fee_Rate_Skips_Fee_Postings()
    {
        await SetSettingAsync(AbpAdmin.Collection.Settings.CollectionSettings.Withdraw.FeeRate, "0");
        await FundAsync(User1, 1000m);
        var order = await Manager.SubmitAsync(User1, CollectionCurrency.Usdt,
            "0x1111111111111111111111111111111111111111", 100m);
        order.Fee.ShouldBe(0m);
        order.ActualAmount.ShouldBe(100m);

        await Manager.ApproveAsync(order.Id, Admin, null);
        (await PlatformBalanceAsync(AbpAdmin.Ledger.Integration.Contracts.FinanceAccountCodes.WithdrawFeeIncome))
            .ShouldBe(0m); // 无零额凭证，收入科目不动
        (await UserFrozenAsync(User1)).ShouldBe(0m);
    }

    // ── 共用 ─────────────────────────────────────────────────────────────────────────────

    private async Task<ColWithdrawOrder> SubmitInNewScopeAsync(
        Guid userId, CollectionCurrency currency, decimal amount)
    {
        using var scope = ServiceProvider.CreateScope();
        var manager = scope.ServiceProvider.GetRequiredService<WithdrawManager>();
        return await manager.SubmitAsync(userId, currency,
            "0x1111111111111111111111111111111111111111", amount);
    }

    private async Task SetSettingAsync(string key, string value)
    {
        await GetRequiredService<ISettingManager>().SetGlobalAsync(key, value);
    }

    private Task<decimal> UserAvailableAsync(Guid userId) =>
        Ledger.GetBalanceAsync(new AccountRef(FinanceAccountCodes.UserAvailable, FinOwnerType.User, userId.ToString()), "USDT");

    private Task<decimal> UserFrozenAsync(Guid userId) =>
        Ledger.GetBalanceAsync(new AccountRef(FinanceAccountCodes.UserFrozen, FinOwnerType.User, userId.ToString()), "USDT");

    private Task<decimal> PlatformBalanceAsync(string code) =>
        Ledger.GetBalanceAsync(new AccountRef(code, FinOwnerType.Platform, ""), "USDT");

    private async Task<ColWithdrawOrder> GetOrderAsync(Guid id)
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.WithdrawOrders.AsNoTracking().FirstAsync(o => o.Id == id);
        });
    }

    private async Task<int> CountOrdersAsync(Guid userId)
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            return await dbContext.WithdrawOrders.AsNoTracking().CountAsync(o => o.UserId == userId);
        });
    }

    private async Task<decimal> BalanceOfAsync(Guid userId)
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var row = await dbContext.Balances.AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Currency == CollectionCurrency.Usdt);
            return row?.Balance ?? 0m;
        });
    }

    private async Task<decimal> FrozenOfAsync(Guid userId)
    {
        return await ReadAsync(async () =>
        {
            var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
            var row = await dbContext.Balances.AsNoTracking()
                .FirstOrDefaultAsync(b => b.UserId == userId && b.Currency == CollectionCurrency.Usdt);
            return row?.Frozen ?? 0m;
        });
    }

    /// <summary>测试置态（绕过状态机；模拟 M3 worker 的迁移结果）。</summary>
    private async Task ForceStatusAsync(Guid orderId, WithdrawStatus status, string? failReason)
    {
        var uowManager = GetRequiredService<IUnitOfWorkManager>();
        using var uow = uowManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await GetRequiredService<IDbContextProvider<CollectionDbContext>>().GetDbContextAsync();
        var o = await dbContext.WithdrawOrders.FirstAsync(x => x.Id == orderId);
        o.Status = status;
        o.FailReason = failReason;
        await dbContext.SaveChangesAsync();
        await uow.CompleteAsync();
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
