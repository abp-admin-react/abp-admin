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
}
