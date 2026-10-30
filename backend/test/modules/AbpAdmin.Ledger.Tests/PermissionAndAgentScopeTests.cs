using System;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Services;
using AbpAdmin.Ledger.Services.Dtos;
using Shouldly;
using Volo.Abp.Authorization;
using Xunit;

namespace AbpAdmin.Ledger;

/// <summary>权限 403：View/Create/Approve/Reverse；代理 Scope.Agent 强制过滤。</summary>
public class PermissionAndAgentScopeTests : LedgerScenarioTestBase
{
    private readonly Guid _agentUser = TestUsers.Agent;
    private readonly Guid _otherAgent = Guid.Parse("d1111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Unauthenticated_Or_Nobody_Cannot_View_Vouchers()
    {
        await Ledger.PostAsync(CreditPay("P-1", 1m, Guid.NewGuid()));
        using (As(TestUsers.Nobody))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() =>
                GetRequiredService<FinVoucherAppService>().GetListAsync(new FinVoucherListInput()));
        }
    }

    [Fact]
    public async Task Agent_Cannot_Reverse_Or_Create_Adjust()
    {
        var id = await Ledger.PostAsync(CreditPay("P-2", 1m, Guid.NewGuid()));
        using (As(TestUsers.Agent))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() =>
                GetRequiredService<FinVoucherAppService>().ReverseAsync(id));
            await Should.ThrowAsync<Exception>(() =>
                GetRequiredService<FinManualAdjustAppService>().CreateAsync(new CreateUpdateFinManualAdjustDto
                {
                    Amount = 1m, Currency = Cny, DebitAccountCode = FinanceAccountCodes.ChannelClearing,
                    DebitOwnerType = FinOwnerType.Channel, DebitOwnerId = "default"
                }));
        }
    }

    [Fact]
    public async Task Boss_Can_View_But_Not_Reverse()
    {
        var id = await Ledger.PostAsync(CreditPay("P-3", 1m, Guid.NewGuid()));
        using (As(TestUsers.Boss))
        {
            (await GetRequiredService<FinVoucherAppService>().GetAsync(id)).Id.ShouldBe(id);
            await Should.ThrowAsync<AbpAuthorizationException>(() =>
                GetRequiredService<FinVoucherAppService>().ReverseAsync(id));
        }
    }

    [Fact]
    public async Task Agent_Scope_Forces_Own_AgentId_On_SubAccount_Balances()
    {
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoCommission, "CA", FinanceBizTypes.Phases.Accrue, Cny, 9m, AgentId: _agentUser));
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoCommission, "CB", FinanceBizTypes.Phases.Accrue, Cny, 4m, AgentId: _otherAgent));

        using (As(TestUsers.AccountantA))
        {
            var all = await GetRequiredService<FinLedgerAppService>().GetSubAccountBalancesAsync(new FinSubAccountBalanceListInput
            {
                AccountCode = FinanceAccountCodes.AgentPayable, MaxResultCount = 100
            });
            all.TotalCount.ShouldBeGreaterThanOrEqualTo(2);
        }

        using (As(TestUsers.Agent))
        {
            // 即使传入他人 AgentId，服务端强制覆盖成当前用户
            var mine = await GetRequiredService<FinLedgerAppService>().GetSubAccountBalancesAsync(new FinSubAccountBalanceListInput
            {
                AccountCode = FinanceAccountCodes.AgentPayable, AgentId = _otherAgent, MaxResultCount = 100
            });
            mine.Items.Count.ShouldBe(1);
            mine.Items[0].OwnerId.ShouldBe(_agentUser.ToString());
            mine.Items[0].Balance.ShouldBe(9m);
        }
    }

    /// <summary>
    /// 回归锁：Ledger.View + Scope.Agent（无 Scope.All）的组合不能只在子户列表路径
    /// 被切片，GetBalanceAsync / GetTrialBalanceAsync / FinOverview 三个读面绕过了
    /// ResolveAgentScopeAsync——切片代理可携带任意 OwnerId 读他人余额 / 全平台试算合计 /
    /// 平台资金总览。修复后：余额与试算强制按自己的 AgentId；平台总览整体拒绝。
    /// </summary>
    [Fact]
    public async Task Agent_Slice_Forced_On_GetBalance_Other_Owner_Returns_Own_Balance()
    {
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoCommission, "CA", FinanceBizTypes.Phases.Accrue, Cny, 9m, AgentId: TestUsers.AgentWithLedgerView));
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoCommission, "CB", FinanceBizTypes.Phases.Accrue, Cny, 4m, AgentId: _otherAgent));

        using (As(TestUsers.AgentWithLedgerView))
        {
            var svc = GetRequiredService<FinLedgerAppService>();
            var forced = await svc.GetBalanceAsync(new GetBalanceInput
            {
                AccountCode = FinanceAccountCodes.AgentPayable,
                OwnerType = FinOwnerType.Agent,
                OwnerId = _otherAgent.ToString(),
                Currency = Cny,
            });
            // 切片键 = 当前登录用户 Id：传他人 OwnerId 必须被强制回自己（d200），拿到自己的 9m 而非他人的 4m
            forced.OwnerId.ShouldBe(TestUsers.AgentWithLedgerView.ToString());
            forced.OwnerType.ShouldBe(FinOwnerType.Agent);
            forced.Balance.ShouldBe(9m, "切片调用方读他人 OwnerId 必须被强制回自己的余额，而非他人余额");
        }

        using (As(TestUsers.AccountantA))
        {
            // 对照：无 Scope.Agent 的会计（也非 Scope.All 的收窄语义）仍按传参读
            var unscoped = await GetRequiredService<FinLedgerAppService>().GetBalanceAsync(new GetBalanceInput
            {
                AccountCode = FinanceAccountCodes.AgentPayable,
                OwnerType = FinOwnerType.Agent,
                OwnerId = _otherAgent.ToString(),
                Currency = Cny,
            });
            unscoped.Balance.ShouldBe(4m);
        }
    }

    [Fact]
    public async Task Agent_Slice_Filters_TrialBalance_And_Refuses_Platform_Overview()
    {
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoCommission, "CA", FinanceBizTypes.Phases.Accrue, Cny, 9m, AgentId: TestUsers.AgentWithLedgerView));
        await Ledger.PostAsync(new FinanceBizEvent(FinanceBizTypes.DemoCommission, "CB", FinanceBizTypes.Phases.Accrue, Cny, 4m, AgentId: _otherAgent));

        using (As(TestUsers.AgentWithLedgerView))
        {
            var svc = GetRequiredService<FinLedgerAppService>();
            var trial = await svc.GetTrialBalanceAsync(new FinTrialBalanceInput { IncludeDemo = true });
            // 佣金计提 = 借 6601（平台属主）+ 贷 2203（代理属主）。切片只保留代理属主行：
            // 自己的贷方 9m 在，借方（平台行）与他人 4m 贷方都不在。
            trial.Lines.ShouldNotBeNull();
            trial.TotalCredit.ShouldBe(9m, "切片后试算只含自己名下子户的分录");
            trial.TotalDebit.ShouldBe(0m, "6601 借方行是平台属主，不在代理切片内");

            await Should.ThrowAsync<AbpAuthorizationException>(() =>
                GetRequiredService<FinOverviewAppService>().GetAsync(),
                "平台资金总览对切片操作员整体拒绝");
        }
    }

    [Fact]
    public async Task Rules_And_Trial_Balance_Require_View_Permissions()
    {
        using (As(TestUsers.Agent))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<FinPostingRuleAppService>().GetListAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => GetRequiredService<FinLedgerAppService>().GetTrialBalanceAsync(new FinTrialBalanceInput()));
        }

        using (As(TestUsers.AccountantA))
        {
            (await GetRequiredService<FinPostingRuleAppService>().GetListAsync()).Items.Count.ShouldBeGreaterThan(0);
            var trial = await GetRequiredService<FinLedgerAppService>().GetTrialBalanceAsync(new FinTrialBalanceInput());
            trial.IsBalanced.ShouldBeTrue();
        }
    }
}
