using System.Collections.Generic;

namespace AbpAdmin.Ledger.Integration.Contracts;

/// <summary>demo / 框架内置 BizType 与 Phase 常量（定稿 §3）。模板按 (BizType, Phase) 匹配；BatchKey 不进规则定位。</summary>
public static class FinanceBizTypes
{
    public const string DemoCredit = "demo.credit";
    public const string DemoRent = "demo.rent";
    public const string DemoCommission = "demo.commission";
    public const string Payment = "payment";
    public const string Wallet = "wallet";
    public const string ManualAdjust = "manual-adjust";

    /// <summary>链上充值入账（归集确认后入账，借 1001 / 贷 2201）。</summary>
    public const string Deposit = "deposit";

    /// <summary>链上提现（freeze/unfreeze/approve/fee-take/payout/refund 六个 phase，见提现过账表）。</summary>
    public const string Withdraw = "withdraw";

    public static class Phases
    {
        public const string Pay = "pay";
        public const string Recognize = "recognize";
        public const string Refund = "refund";
        public const string Accrue = "accrue";
        public const string Settle = "settle";
        public const string Freeze = "freeze";
        public const string Settled = "settled";
        public const string Post = "post";

        /// <summary>提现审核通过：冻结 → 渠道清算（净额出账）。</summary>
        public const string Approve = "approve";

        /// <summary>提现手续费实现：冻结 → 手续费收入。</summary>
        public const string FeeTake = "fee-take";

        /// <summary>提现打款完成：渠道清算 → 链上资产（真实资金流出）。</summary>
        public const string Payout = "payout";

        /// <summary>提现撤单/拒绝：冻结 → 可用（与 freeze 对称）。</summary>
        public const string Unfreeze = "unfreeze";

        /// <summary>提现失败退手续费：手续费收入 → 用户可用（与 fee-take 对称）。</summary>
        public const string FeeRefund = "fee-refund";
    }

    /// <summary>
    /// A1 资金总览的方向词表（资金口径，OCR E2E 复核修正）：只计真实资金流动——
    /// 流入 = 收款及其完成（pay/settled）；流出 = 退款（refund）与提现打款（payout）。
    /// recognize（预收转收入）/accrue/settle（代币核销）/freeze/post/unfreeze/approve/fee-take
    /// 是内部转移，不产生资金流，计入会把同一笔钱重复计入「今日收款」（pay 贷预收 + recognize 贷收入 双计）。
    /// 新增 phase 时改这一处 + 过账模板，总览口径自动跟上。
    /// </summary>
    public static readonly IReadOnlyList<string> IncomePhases = new[] { Phases.Pay, Phases.Settled };
    public static readonly IReadOnlyList<string> ExpensePhases = new[] { Phases.Refund, Phases.Payout };
}
