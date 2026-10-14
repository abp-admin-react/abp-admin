using System.Collections.Generic;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Ledger.Data;

/// <summary>
/// 框架级种子：只含与行业无关的通用科目，以及「收款」「确认收入」两个通用模板和人工调账模板（A3 调账页依赖）。
/// 行业科目/模板由业务模块通过 <see cref="ILedgerSeedProvider"/> 自行注册。
/// </summary>
public class FrameworkLedgerSeedProvider : ILedgerSeedProvider, ITransientDependency
{
    public IEnumerable<LedgerAccountSeed> Accounts { get; } = new LedgerAccountSeed[]
    {
        new(FinanceAccountCodes.ChannelClearing, "货币资金（现金/银行）", FinAccountType.Asset, FinNormalSide.Debit),
        new(FinanceAccountCodes.AccountsReceivable, "应收账款", FinAccountType.Asset, FinNormalSide.Debit),
        new(FinanceAccountCodes.AccountsPayable, "应付账款", FinAccountType.Liability, FinNormalSide.Credit),
        new(FinanceAccountCodes.Unearned, "预收账款", FinAccountType.Liability, FinNormalSide.Credit),
        new(FinanceAccountCodes.OpeningEquity, "实收资本（股本）", FinAccountType.Equity, FinNormalSide.Credit),
        new(FinanceAccountCodes.ManualAdjust, "人工调整", FinAccountType.Equity, FinNormalSide.Credit),
        new(FinanceAccountCodes.RetainedEarnings, "未分配利润", FinAccountType.Equity, FinNormalSide.Credit),
        new(FinanceAccountCodes.ServiceIncome, "主营业务收入", FinAccountType.Income, FinNormalSide.Credit),
        new(FinanceAccountCodes.MainCost, "主营业务成本", FinAccountType.Expense, FinNormalSide.Debit),
        new(FinanceAccountCodes.GeneralExpense, "管理费用", FinAccountType.Expense, FinNormalSide.Debit),
    };

    public IEnumerable<LedgerRuleSeed> Rules { get; } = new LedgerRuleSeed[]
    {
        new(FinanceBizTypes.DemoCredit, FinanceBizTypes.Phases.Pay,
            "收款：借货币资金 / 贷预收账款",
            """[{"debit":"1002","credit":"2205","note":"receive"}]"""),
        new(FinanceBizTypes.DemoCredit, FinanceBizTypes.Phases.Recognize,
            "确认收入：借预收账款 / 贷主营业务收入（按用量/批次）",
            """[{"debit":"2205","credit":"6002","note":"recognize"}]"""),
        new(FinanceBizTypes.ManualAdjust, FinanceBizTypes.Phases.Post,
            "人工调账过账：借贷双方取自调账单（贷方缺省 3101 人工调整）",
            """[{"debit":"*","credit":"*","note":"manual-adjust"}]""", 2),
    };
}
