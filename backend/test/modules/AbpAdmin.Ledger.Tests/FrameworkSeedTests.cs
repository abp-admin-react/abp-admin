using System.Linq;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using Shouldly;
using Xunit;

namespace AbpAdmin.Ledger;

/// <summary>框架种子不得含行业科目/模板（海峰 2026-10-14：框架级没有租赁、返佣）。</summary>
public class FrameworkSeedTests
{
    private static readonly string[] BusinessCodes =
    {
        FinanceAccountCodes.OnChainAsset, FinanceAccountCodes.UserAvailable, FinanceAccountCodes.UserFrozen,
        FinanceAccountCodes.AgentPayable, FinanceAccountCodes.RentIncome, FinanceAccountCodes.CommissionExpense,
        FinanceAccountCodes.WithdrawFeeIncome, // 6003：Collection 提现播种的业务科目（OCR 补锁）
    };

    private static readonly string[] BusinessWords =
        { "租赁", "租金", "返佣", "佣金", "分成", "代理", "钱包", "rent", "commission", "wallet", "agent" };

    [Fact]
    public void Framework_Seed_Has_No_Business_Accounts()
    {
        var p = new FrameworkLedgerSeedProvider();
        p.Accounts.Select(a => a.Code).Intersect(BusinessCodes).ShouldBeEmpty();
        foreach (var a in p.Accounts)
        {
            BusinessWords.ShouldNotContain(w => a.Name.Contains(w, System.StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Framework_Seed_Rules_Are_Only_Receive_Recognize_And_ManualAdjust()
    {
        var p = new FrameworkLedgerSeedProvider();
        p.Rules.Select(r => (r.BizType, r.Phase)).ToArray().ShouldBe(new[]
        {
            (FinanceBizTypes.DemoCredit, FinanceBizTypes.Phases.Pay),
            (FinanceBizTypes.DemoCredit, FinanceBizTypes.Phases.Recognize),
            (FinanceBizTypes.ManualAdjust, FinanceBizTypes.Phases.Post),
        }, ignoreOrder: true);
        foreach (var r in p.Rules)
        {
            BusinessWords.ShouldNotContain(w =>
                r.Description.Contains(w, System.StringComparison.OrdinalIgnoreCase)
                || r.TemplateJson.Contains(w, System.StringComparison.OrdinalIgnoreCase));
            BusinessCodes.ShouldNotContain(c => r.TemplateJson.Contains("\"" + c + "\""));
        }
    }
}
