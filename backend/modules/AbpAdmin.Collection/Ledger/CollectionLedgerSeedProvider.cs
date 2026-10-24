using System.Collections.Generic;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Collection.Ledger;

/// <summary>
/// 收款/提现接入 Fin 账本的科目与过账模板种子（提现 M2，首个真实 F3 业务消费者）。
/// 科目：1001 链上资产、2201 用户可用、2202 用户冻结（框架不种，本模块种）、6003 提现手续费收入。
/// 待付中转用框架已种的 2241 应付账款（1002 渠道清算是资产类借方科目，审核通过时贷记它
/// 等于链上资产提前减少——账务方向错误，故提现待付走 2241）。
/// 模板与状态机一一对应：deposit/pay 充值入账；withdraw 六步 + fee-refund；wallet/post 存量开账回填。
/// </summary>
public class CollectionLedgerSeedProvider : ILedgerSeedProvider, ITransientDependency
{
    public IEnumerable<LedgerAccountSeed> Accounts { get; } = new LedgerAccountSeed[]
    {
        new(FinanceAccountCodes.OnChainAsset, "链上资产", FinAccountType.Asset, FinNormalSide.Debit),
        new(FinanceAccountCodes.UserAvailable, "用户可用余额", FinAccountType.Liability, FinNormalSide.Credit),
        new(FinanceAccountCodes.UserFrozen, "用户冻结余额", FinAccountType.Liability, FinNormalSide.Credit),
        new(FinanceAccountCodes.WithdrawFeeIncome, "提现手续费收入", FinAccountType.Income, FinNormalSide.Credit),
    };

    public IEnumerable<LedgerRuleSeed> Rules { get; } = new LedgerRuleSeed[]
    {
        // 充值入账（归集确认同事务过账）：链上资产增、欠用户增
        new(FinanceBizTypes.Deposit, FinanceBizTypes.Phases.Pay, "充值入账",
            "[{\"debit\":\"1001\",\"credit\":\"2201\",\"note\":\"充值入账\"}]"),

        // 提现状态机：冻结/解冻是负债内部重分类；审核把负债转应付；打款清应付减链上资产
        new(FinanceBizTypes.Withdraw, FinanceBizTypes.Phases.Freeze, "提现冻结：可用→冻结",
            "[{\"debit\":\"2201\",\"credit\":\"2202\",\"note\":\"提现冻结\"}]"),
        new(FinanceBizTypes.Withdraw, FinanceBizTypes.Phases.Unfreeze, "提现解冻：冻结→可用",
            "[{\"debit\":\"2202\",\"credit\":\"2201\",\"note\":\"提现解冻\"}]"),
        new(FinanceBizTypes.Withdraw, FinanceBizTypes.Phases.Approve, "提现审核：冻结→应付",
            "[{\"debit\":\"2202\",\"credit\":\"2241\",\"note\":\"提现审核通过\"}]"),
        new(FinanceBizTypes.Withdraw, FinanceBizTypes.Phases.FeeTake, "提现手续费：冻结→收入",
            "[{\"debit\":\"2202\",\"credit\":\"6003\",\"note\":\"提现手续费\"}]"),
        new(FinanceBizTypes.Withdraw, FinanceBizTypes.Phases.Payout, "提现打款：应付→链上资产",
            "[{\"debit\":\"2241\",\"credit\":\"1001\",\"note\":\"提现打款完成\"}]"),
        new(FinanceBizTypes.Withdraw, FinanceBizTypes.Phases.Refund, "提现失败退款：应付→可用",
            "[{\"debit\":\"2241\",\"credit\":\"2201\",\"note\":\"提现退款\"}]"),
        new(FinanceBizTypes.Withdraw, FinanceBizTypes.Phases.FeeRefund, "提现退手续费：收入→可用",
            "[{\"debit\":\"6003\",\"credit\":\"2201\",\"note\":\"提现退手续费\"}]"),

        // 存量余额开账回填（管理端一次性，BizRefId = opening:{userId}:{currency}）
        new(FinanceBizTypes.Wallet, FinanceBizTypes.Phases.Post, "存量余额开账",
            "[{\"debit\":\"1001\",\"credit\":\"2201\",\"note\":\"存量余额开账\"}]"),
    };
}
