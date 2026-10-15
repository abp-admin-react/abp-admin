using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Configuration;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Posting;
using AbpAdmin.Ledger.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Volo.Abp;
using Volo.Abp.SettingManagement;
using Xunit;

namespace AbpAdmin.Ledger;

/// <summary>定稿 §2/§3/§10：事件→模板→GL、预收→收入、幂等、借贷平衡、非负、开关。</summary>
public class PostingEngineTests : LedgerScenarioTestBase
{
    private readonly Guid _user = Guid.NewGuid();

    [Fact]
    public async Task Posting_Ignores_Period_Status_F1_Contract_Pinned()
    {
        // F1 契约钉住（OCR test 修复）：过账引擎不检查 FinPeriod.Status——A4 期间页只做展示，
        // 本阶段无关账语义。把当期置 Closed 后过账仍成功；将来引入关账校验时本用例必红，
        // 逼一次显式决策（改语义 + 改 A4 + 改这里），而不是悄悄改掉。
        // 直读 DbContext 须自开 UoW（Provider 只能在 UoW 内解析）；期间行找不到（跨月边界跑 CI）
        // 则跳过置 Closed——引擎会自建当期，核心断言（照常过账）不受影响。置 Closed 不回滚：
        // 引擎按契约无视 Status，无其他用例断言期间状态
        using (var uow = GetRequiredService<Volo.Abp.Uow.IUnitOfWorkManager>().Begin(new Volo.Abp.Uow.AbpUnitOfWorkOptions()))
        {
            var db = await GetRequiredService<Volo.Abp.EntityFrameworkCore.IDbContextProvider<LedgerDbContext>>().GetDbContextAsync();
            var now = DateTime.UtcNow;
            var period = await db.Periods.FirstOrDefaultAsync(p => p.Year == now.Year && p.Month == now.Month);
            if (period != null)
            {
                period.Status = FinPeriodStatus.Closed;
                await db.SaveChangesAsync();
            }

            await uow.CompleteAsync();
        }

        var id = await Ledger.PostAsync(CreditPay("C-PERIOD-PIN", 5m, _user));
        var voucher = (await VouchersAsync("C-PERIOD-PIN")).Single();
        voucher.PeriodId.ShouldNotBeNull();
        voucher.Status.ShouldBe(FinVoucherStatus.Posted);
    }

    [Fact]
    public async Task Event_Template_GL_DemoCredit_Pay_Posts_Debit1002_Credit2205()
    {
        var id = await Ledger.PostAsync(CreditPay("C-1", 100m, _user) with { Dimensions = new Dictionary<string, string> { ["channel"] = "wechat" } });

        var voucher = (await VouchersAsync("C-1")).Single();
        voucher.Id.ShouldBe(id);
        voucher.Status.ShouldBe(FinVoucherStatus.Posted);
        voucher.BatchKey.ShouldBe("");
        voucher.RuleVersion.ShouldBe(1);
        voucher.IsDemo.ShouldBeTrue();
        voucher.UserId.ShouldBe(_user);
        voucher.PeriodId.ShouldNotBeNull();

        var entries = await EntriesAsync(id);
        entries.Count.ShouldBe(2);
        entries.Sum(e => e.Debit).ShouldBe(entries.Sum(e => e.Credit));
        entries[0].Debit.ShouldBe(100m);
        entries[1].Credit.ShouldBe(100m);
        entries[0].Dimensions!.ShouldContain("wechat");

        (await ChannelBalance("wechat")).ShouldBe(100m);
        (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(100m);
        await AssertTrialBalancedAsync();
        await AssertProjectionMatchesEntriesAsync();
    }

    [Fact]
    public async Task Prepaid_To_Income_Credit_Pay_Then_Multi_Batch_Recognize()
    {
        await Ledger.PostAsync(CreditPay("C-2", 100m, _user));
        var d1 = await Ledger.PostAsync(CreditRecognize("C-2", 30m, _user, "2026-10-14"));
        var d2 = await Ledger.PostAsync(CreditRecognize("C-2", 20m, _user, "2026-10-15"));
        var d1Retry = await Ledger.PostAsync(CreditRecognize("C-2", 30m, _user, "2026-10-14"));

        d1Retry.ShouldBe(d1);
        d2.ShouldNotBe(d1);
        (await VouchersAsync("C-2")).Count.ShouldBe(3);
        (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(50m);
        (await PlatformBalance(FinanceAccountCodes.ServiceIncome)).ShouldBe(50m);
        (await PlatformBalance(FinanceAccountCodes.RentIncome)).ShouldBe(0m);
        await AssertTrialBalancedAsync();
        await AssertProjectionMatchesEntriesAsync();
    }

    [Fact]
    public async Task Prepaid_To_Income_Rent_Pay_From_Available_Then_Recognize_RentIncome()
    {
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.Payment, "P-1", FinanceBizTypes.Phases.Settled, Cny, 300m, UserId: _user));
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoRent, "R-1", FinanceBizTypes.Phases.Pay, Cny, 300m, UserId: _user));
        (await UserBalance(FinanceAccountCodes.UserAvailable, _user)).ShouldBe(0m);
        (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(300m);

        for (var day = 1; day <= 3; day++)
        {
            await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoRent, "R-1", FinanceBizTypes.Phases.Recognize, Cny, 10m,
                UserId: _user, BatchKey: $"d{day:000}"));
        }

        (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(270m);
        (await PlatformBalance(FinanceAccountCodes.RentIncome)).ShouldBe(30m);
        await AssertTrialBalancedAsync();
    }

    [Fact]
    public async Task Pay_Never_Credits_Income_Directly()
    {
        await Ledger.PostAsync(CreditPay("C-3", 80m, _user));
        (await PlatformBalance(FinanceAccountCodes.ServiceIncome)).ShouldBe(0m);
        (await PlatformBalance(FinanceAccountCodes.RentIncome)).ShouldBe(0m);
    }

    [Fact]
    public async Task Commission_Accrue_Requires_AgentId()
    {
        var ex = await Should.ThrowAsync<BusinessException>(() => Ledger.PostAsync(
            new FinanceBizEvent(FinanceBizTypes.DemoCommission, "M-1", FinanceBizTypes.Phases.Accrue, Cny, 5m)));
        ex.Code.ShouldBe(FinanceErrorCodes.AgentIdRequired);
        (await VouchersAsync("M-1")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Commission_Accrue_And_Settle_Attributes_2203_To_Agent()
    {
        var agentA = Guid.NewGuid();
        var agentB = Guid.NewGuid();
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoCommission, "M-2", FinanceBizTypes.Phases.Accrue, Cny, 12m, AgentId: agentA));
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoCommission, "M-3", FinanceBizTypes.Phases.Accrue, Cny, 7m, AgentId: agentB));
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoCommission, "M-2", FinanceBizTypes.Phases.Settle, Cny, 2m, AgentId: agentA));

        (await AgentBalance(agentA)).ShouldBe(10m);
        (await AgentBalance(agentB)).ShouldBe(7m);
        (await PlatformBalance(FinanceAccountCodes.CommissionExpense)).ShouldBe(19m);
        var voucher = (await VouchersAsync("M-2")).First();
        voucher.AgentId.ShouldBe(agentA);
        await AssertTrialBalancedAsync();
    }

    [Fact]
    public async Task Idempotent_Replay_Returns_Same_Voucher_And_Does_Not_Double_Post()
    {
        var first = await Ledger.PostAsync(CreditPay("C-4", 10.50m, _user));
        var second = await Ledger.PostAsync(CreditPay("C-4", 10.5000m, _user));
        second.ShouldBe(first);
        (await VouchersAsync("C-4")).Count.ShouldBe(1);
        (await EntriesAsync(first)).Count.ShouldBe(2);
        (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(10.5m);
    }

    [Fact]
    public async Task Idempotent_Same_Key_Different_Content_Is_Conflict()
    {
        await Ledger.PostAsync(CreditPay("C-5", 10m, _user));
        var ex = await Should.ThrowAsync<BusinessException>(() => Ledger.PostAsync(CreditPay("C-5", 11m, _user)));
        ex.Code.ShouldBe(FinanceErrorCodes.IdempotencyConflict);
        var ex2 = await Should.ThrowAsync<BusinessException>(() => Ledger.PostAsync(CreditPay("C-5", 10m, Guid.NewGuid())));
        ex2.Code.ShouldBe(FinanceErrorCodes.IdempotencyConflict);
        (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(10m);
    }

    [Fact]
    public async Task Idempotent_Concurrent_Replay_Posts_Exactly_Once()
    {
        var evt = CreditPay("C-6", 25m, _user);
        var ids = new System.Collections.Concurrent.ConcurrentBag<Guid>();
        await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => RunInNewUowAsync(async sp =>
            ids.Add(await sp.GetRequiredService<ILedgerIntegrationService>().PostAsync(evt)))));

        ids.Count.ShouldBe(12);
        ids.Distinct().Count().ShouldBe(1);
        (await VouchersAsync("C-6")).Count.ShouldBe(1);
        (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(25m);
        await AssertProjectionMatchesEntriesAsync();
    }

    [Fact]
    public async Task Concurrent_Distinct_Batches_On_Same_SubAccount_Keep_Balance_Exact()
    {
        await Ledger.PostAsync(CreditPay("C-7", 100m, _user));
        await Task.WhenAll(Enumerable.Range(1, 10).Select(i => RunInNewUowAsync(sp =>
            sp.GetRequiredService<ILedgerIntegrationService>().PostAsync(CreditRecognize("C-7", 3m, _user, $"b{i}")))));

        (await VouchersAsync("C-7")).Count.ShouldBe(11);
        (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(70m);
        (await PlatformBalance(FinanceAccountCodes.ServiceIncome)).ShouldBe(30m);
        await AssertProjectionMatchesEntriesAsync();
        await AssertTrialBalancedAsync();
    }

    [Fact]
    public async Task Concurrent_First_Post_For_New_SubAccounts_Does_Not_Duplicate_SubAccounts()
    {
        var users = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToList();
        await Task.WhenAll(users.Select((u, i) => RunInNewUowAsync(sp =>
            sp.GetRequiredService<ILedgerIntegrationService>().PostAsync(CreditPay($"C-8-{i}", 1m, u)))));

        var channelSubs = await ReadAsync(db => db.SubAccounts.CountAsync(s => s.OwnerType == FinOwnerType.Channel && s.OwnerId == "default"));
        channelSubs.ShouldBe(1);
        (await ChannelBalance()).ShouldBe(8m);
    }

    [Fact]
    public async Task Overdrawing_User_SubAccount_Is_Rejected_And_Rolled_Back()
    {
        await Ledger.PostAsync(CreditPay("C-9", 10m, _user));
        var ex = await Should.ThrowAsync<BusinessException>(() => Ledger.PostAsync(CreditRecognize("C-9", 10.01m, _user, "x")));
        ex.Code.ShouldBe(FinanceErrorCodes.InsufficientBalance);
        (await VouchersAsync("C-9")).Count.ShouldBe(1);
        (await UserBalance(FinanceAccountCodes.Unearned, _user)).ShouldBe(10m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_Positive_Amount_Is_Rejected(decimal amount)
    {
        var ex = await Should.ThrowAsync<BusinessException>(() => Ledger.PostAsync(CreditPay("C-10", amount, _user)));
        ex.Code.ShouldBe(FinanceErrorCodes.InvalidEvent);
    }

    [Fact]
    public async Task Reserved_Reversal_BatchKey_Prefix_Is_Rejected()
    {
        var ex = await Should.ThrowAsync<BusinessException>(() => Ledger.PostAsync(CreditPay("C-11", 1m, _user, "rev:abc")));
        ex.Code.ShouldBe(FinanceErrorCodes.InvalidEvent);
    }

    [Fact]
    public async Task Unknown_Rule_Is_Rejected()
    {
        var ex = await Should.ThrowAsync<BusinessException>(() => Ledger.PostAsync(
            new FinanceBizEvent("demo.unknown", "U-1", "pay", Cny, 1m, UserId: _user)));
        ex.Code.ShouldBe(FinanceErrorCodes.PostingRuleNotFound);
    }

    [Fact]
    public async Task Missing_UserId_For_User_Account_Is_Rejected()
    {
        var ex = await Should.ThrowAsync<BusinessException>(() => Ledger.PostAsync(
            new FinanceBizEvent(FinanceBizTypes.DemoCredit, "C-12", FinanceBizTypes.Phases.Pay, Cny, 1m)));
        ex.Code.ShouldBe(FinanceErrorCodes.InvalidEvent);
    }

    [Fact]
    public async Task Ledger_Disabled_Setting_Rejects_Business_Post()
    {
        await GetRequiredService<ISettingManager>().SetGlobalAsync(LedgerSettings.Enabled, "false");
        try
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => Ledger.PostAsync(CreditPay("C-13", 1m, _user)));
            ex.Code.ShouldBe(FinanceErrorCodes.LedgerDisabled);
        }
        finally
        {
            await GetRequiredService<ISettingManager>().SetGlobalAsync(LedgerSettings.Enabled, "true");
        }
    }

    [Fact]
    public async Task BizType_Gray_Switch_Rejects_Only_That_Type()
    {
        var options = GetRequiredService<IOptions<LedgerOptions>>().Value;
        options.BizTypes[FinanceBizTypes.DemoRent] = false;
        try
        {
            var ex = await Should.ThrowAsync<BusinessException>(() => Ledger.PostAsync(
                new FinanceBizEvent(FinanceBizTypes.DemoRent, "R-2", FinanceBizTypes.Phases.Pay, Cny, 1m, UserId: _user)));
            ex.Code.ShouldBe(FinanceErrorCodes.LedgerDisabled);
            await Ledger.PostAsync(CreditPay("C-14", 1m, _user));
        }
        finally
        {
            options.BizTypes.Remove(FinanceBizTypes.DemoRent);
        }
    }

    [Fact]
    public async Task Posting_Outside_Transaction_Is_Refused()
    {
        var engine = GetRequiredService<LedgerPostingEngine>();
        await Should.ThrowAsync<AbpException>(() => WithUnitOfWorkAsync(
            new Volo.Abp.Uow.AbpUnitOfWorkOptions(isTransactional: false),
            () => engine.PostAsync(CreditPay("C-15", 1m, _user))));
        (await VouchersAsync("C-15")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Host_Voucher_UK_Is_Nulls_Not_Distinct()
    {
        await Ledger.PostAsync(CreditPay("C-16", 1m, _user));
        await Should.ThrowAsync<Exception>(() => WithUnitOfWorkAsync(async () =>
        {
            var db = await GetRequiredService<Volo.Abp.EntityFrameworkCore.IDbContextProvider<Data.LedgerDbContext>>().GetDbContextAsync();
            var dup = new FinVoucher(Guid.NewGuid(), FinanceBizTypes.DemoCredit, "C-16", FinanceBizTypes.Phases.Pay, Cny, 1m) { Fingerprint = "x" };
            db.Vouchers.Add(dup);
            await db.SaveChangesAsync();
        }));
    }
}

/// <summary>借贷平衡守卫（纯单测）。</summary>
public class BalanceGuardTests
{
    private static readonly FinAccount A = new(Guid.NewGuid(), "1002", "a", FinAccountType.Asset, FinNormalSide.Debit);
    private static readonly FinAccount B = new(Guid.NewGuid(), "2205", "b", FinAccountType.Liability, FinNormalSide.Credit);

    private static PostingLine L(FinAccount a, decimal d, decimal c) => new(a, FinOwnerType.Platform, "", "", d, c, null);

    public static IEnumerable<object[]> Unbalanced() => new[]
    {
        new object[] { new[] { L(A, 10, 0), L(B, 0, 9) } },
        new object[] { new[] { L(A, 10, 0) } },
        new object[] { new[] { L(A, 10, 10), L(B, 0, 0) } },
        new object[] { new[] { L(A, -5, 0), L(B, 0, -5) } },
        new object[] { new[] { L(A, 0, 0), L(B, 0, 0) } },
        new object[] { new[] { L(A, 10, 0), L(A, 5, 0), L(B, 0, 14.99m) } },
    };

    [Theory]
    [MemberData(nameof(Unbalanced))]
    public void Unbalanced_Lines_Are_Rejected(PostingLine[] lines)
    {
        var ex = Should.Throw<BusinessException>(() => LedgerPostingEngine.EnsureBalanced(lines));
        ex.Code.ShouldBe(FinanceErrorCodes.UnbalancedVoucher);
    }

    [Fact]
    public void Balanced_Multi_Line_Passes()
    {
        LedgerPostingEngine.EnsureBalanced(new[] { L(A, 10, 0), L(A, 5, 0), L(B, 0, 15) });
    }

    [Fact]
    public void Fingerprint_Normalizes_Amount_Scale()
    {
        var u = Guid.NewGuid();
        LedgerPostingEngine.Fingerprint(new FinanceBizEvent("t", "r", "p", "CNY", 10m, u))
            .ShouldBe(LedgerPostingEngine.Fingerprint(new FinanceBizEvent("t", "r", "p", "CNY", 10.000m, u)));
        LedgerPostingEngine.Fingerprint(new FinanceBizEvent("t", "r", "p", "CNY", 10m, u))
            .ShouldNotBe(LedgerPostingEngine.Fingerprint(new FinanceBizEvent("t", "r", "p", "USD", 10m, u)));
    }

    [Fact]
    public void Template_Parse_Rejects_Empty()
    {
        Should.Throw<BusinessException>(() => PostingRuleProvider.Parse("[]"));
        PostingRuleProvider.Parse("""[{"debit":"6601","credit":"2203","requireAgentId":true}]""")
            .Single().RequireAgentId.ShouldBeTrue();
    }
}
