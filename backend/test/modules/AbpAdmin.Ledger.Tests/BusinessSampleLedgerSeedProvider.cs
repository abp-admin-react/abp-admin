using System.Collections.Generic;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Ledger;

/// <summary>
/// 测试专用：模拟「业务模块接入 Ledger」——经 <see cref="ILedgerSeedProvider"/> 扩展点注册租赁/返佣/支付/钱包科目与模板。
/// 只在测试程序集里，框架宿主不会加载。
/// </summary>
public class BusinessSampleLedgerSeedProvider : ILedgerSeedProvider, ITransientDependency
{
    public IEnumerable<LedgerAccountSeed> Accounts { get; } = new LedgerAccountSeed[]
    {
        new(FinanceAccountCodes.OnChainAsset, "链上资产", FinAccountType.Asset, FinNormalSide.Debit),
        new(FinanceAccountCodes.UserAvailable, "用户余额-可用", FinAccountType.Liability, FinNormalSide.Credit),
        new(FinanceAccountCodes.UserFrozen, "用户余额-冻结", FinAccountType.Liability, FinNormalSide.Credit),
        new(FinanceAccountCodes.AgentPayable, "代理应付", FinAccountType.Liability, FinNormalSide.Credit),
        new(FinanceAccountCodes.RentIncome, "租赁收入", FinAccountType.Income, FinNormalSide.Credit),
        new(FinanceAccountCodes.CommissionExpense, "返佣支出", FinAccountType.Expense, FinNormalSide.Debit),
    };

    public IEnumerable<LedgerRuleSeed> Rules { get; } = new LedgerRuleSeed[]
    {
        new(FinanceBizTypes.DemoRent, FinanceBizTypes.Phases.Pay, "付租进预收",
            """[{"debit":"2201","credit":"2205","note":"rent pay"}]"""),
        new(FinanceBizTypes.DemoRent, FinanceBizTypes.Phases.Recognize, "按日/按期确认租赁收入",
            """[{"debit":"2205","credit":"6001","note":"rent recognize"}]"""),
        new(FinanceBizTypes.DemoRent, FinanceBizTypes.Phases.Refund, "租金退款",
            """[{"debit":"2205","credit":"2201","note":"rent refund"}]"""),
        new(FinanceBizTypes.DemoCommission, FinanceBizTypes.Phases.Accrue, "佣金计提（必带 AgentId）",
            """[{"debit":"6601","credit":"2203","note":"commission accrue","requireAgentId":true}]"""),
        new(FinanceBizTypes.DemoCommission, FinanceBizTypes.Phases.Settle, "佣金结算",
            """[{"debit":"2203","credit":"1002","note":"commission settle"}]"""),
        new(FinanceBizTypes.Payment, FinanceBizTypes.Phases.Settled, "支付到账：借渠道清算 / 贷可用",
            """[{"debit":"1002","credit":"2201","note":"payment settled"}]"""),
        new(FinanceBizTypes.Wallet, FinanceBizTypes.Phases.Freeze, "冻结：借可用 / 贷冻结",
            """[{"debit":"2201","credit":"2202","note":"wallet freeze"}]"""),
    };
}
