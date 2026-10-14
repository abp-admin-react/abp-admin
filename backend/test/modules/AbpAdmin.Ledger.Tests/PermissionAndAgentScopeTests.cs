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
