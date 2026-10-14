
using System;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Services;
using AbpAdmin.Ledger.Services.Dtos;
using Shouldly;
using Volo.Abp.Authorization;
using Volo.Abp;
using Xunit;

namespace AbpAdmin.Ledger;

public class ReconcileAndBizDocTests : LedgerScenarioTestBase
{
    [Fact]
    public async Task Reconcile_Defaults_Exclude_Demo_And_Supports_IncludeDemo()
    {
        var user = Guid.NewGuid();
        await Ledger.PostAsync(CreditPay("RC-1", 10m, user)); // IsDemo=true

        using (As(TestUsers.AccountantA))
        {
            var svc = GetRequiredService<FinReconcileAppService>();
            var excl = await svc.RunAsync(new FinReconcileRunInput { IncludeDemo = false });
            excl.IncludeDemo.ShouldBeFalse();
            // 无真实（非 demo）分录 → 平衡、无差异
            excl.HasDiff.ShouldBeFalse();

            var incl = await svc.RunAsync(new FinReconcileRunInput { IncludeDemo = true });
            incl.IncludeDemo.ShouldBeTrue();
            incl.HasDiff.ShouldBeFalse(); // demo 也借贷平衡

            var listDefault = await svc.GetListAsync(new FinReconcileListInput());
            listDefault.Items.ShouldAllBe(i => !i.IncludeDemo);

            var listAll = await svc.GetListAsync(new FinReconcileListInput { IncludeDemo = true });
            listAll.TotalCount.ShouldBeGreaterThanOrEqualTo(2);
        }
    }

    [Fact]
    public async Task Reconcile_Requires_Ledger_View()
    {
        using (As(TestUsers.Agent))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() =>
                GetRequiredService<FinReconcileAppService>().GetListAsync(new FinReconcileListInput()));
        }
    }

    [Fact]
    public async Task BizDoc_Agent_Scope_Forces_Own_AgentId()
    {
        var agent = TestUsers.Agent;
        var other = Guid.NewGuid();
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoCommission, "BD-A", FinanceBizTypes.Phases.Accrue, Cny, 3m, AgentId: agent));
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoCommission, "BD-B", FinanceBizTypes.Phases.Accrue, Cny, 4m, AgentId: other));

        using (As(TestUsers.Agent))
        {
            var list = await GetRequiredService<FinBizDocAppService>().GetListAsync(new FinBizDocListInput
            {
                AgentId = other, MaxResultCount = 50
            });
            list.Items.ShouldAllBe(i => i.AgentId == agent);
            list.TotalCount.ShouldBe(1);
        }
    }

    [Fact]
    public async Task Reconcile_Filters_By_Voucher_PostedTime_HalfOpen_Utc()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        await Ledger.PostAsync(CreditPay("RC-RANGE", 7m, Guid.NewGuid()));
        var after = DateTime.UtcNow.AddSeconds(1);

        using (As(TestUsers.AccountantA))
        {
            var svc = GetRequiredService<FinReconcileAppService>();

            var hit = await svc.RunAsync(new FinReconcileRunInput { IncludeDemo = true, RangeStart = before, RangeEnd = after });
            Debit(hit.DiffJson).ShouldBeGreaterThanOrEqualTo(7m);
            hit.ScopeJson!.ShouldContain("\"rangeStart\":\"");

            var miss = await svc.RunAsync(new FinReconcileRunInput { IncludeDemo = true, RangeStart = before.AddDays(-2), RangeEnd = before.AddDays(-1) });
            Debit(miss.DiffJson).ShouldBe(0m);

            var openEnd = await svc.RunAsync(new FinReconcileRunInput { IncludeDemo = true, RangeStart = after.AddDays(1) });
            Debit(openEnd.DiffJson).ShouldBe(0m);
        }
    }

    [Fact]
    public async Task Reconcile_Invalid_Range_Is_400_Code()
    {
        using (As(TestUsers.AccountantA))
        {
            var svc = GetRequiredService<FinReconcileAppService>();
            var now = DateTime.UtcNow;
            (await Should.ThrowAsync<BusinessException>(() => svc.RunAsync(new FinReconcileRunInput { RangeStart = now, RangeEnd = now })))
                .Code.ShouldBe(FinanceErrorCodes.ReconcileInvalidRange);
            (await Should.ThrowAsync<BusinessException>(() => svc.RunAsync(new FinReconcileRunInput
                {
                    RangeStart = DateTime.SpecifyKind(now, DateTimeKind.Unspecified),
                })))
                .Code.ShouldBe(FinanceErrorCodes.ReconcileInvalidRange);
        }
    }

    private static decimal Debit(string? diffJson) =>
        System.Text.Json.JsonDocument.Parse(diffJson!).RootElement.GetProperty("debit").GetDecimal();
}
