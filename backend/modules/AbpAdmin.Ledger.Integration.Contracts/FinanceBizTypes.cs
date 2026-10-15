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
    }

    /// <summary>
    /// A1 资金总览的方向词表（资金口径，OCR E2E 复核修正）：只计真实资金流动——
    /// 流入 = 收款及其完成（pay/settled）；流出 = 退款（refund）。
    /// recognize（预收转收入）/accrue/settle（代币核销）/freeze/post 是内部转移，不产生资金流，
    /// 计入会把同一笔钱重复计入「今日收款」（pay 贷预收 + recognize 贷收入 双计）。
    /// 新增 phase 时改这一处 + 过账模板，总览口径自动跟上。
    /// </summary>
    public static readonly IReadOnlyList<string> IncomePhases = new[] { Phases.Pay, Phases.Settled };
    public static readonly IReadOnlyList<string> ExpensePhases = new[] { Phases.Refund };
}
