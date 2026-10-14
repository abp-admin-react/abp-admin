namespace AbpAdmin.Ledger.Integration.Contracts;

/// <summary>财务层错误码（与定稿 §5 对齐；前端按码映射文案）。HTTP 状态在 Ledger 模块统一映射。</summary>
public static class FinanceErrorCodes
{
    public const string Namespace = "Finance";

    /// <summary>审批/驳回自己提交的调账但没有 Finance.Adjust.ApproveOwn。HTTP 403。</summary>
    public const string AdjustSelfApproveNotAllowed = Namespace + ":Adjust:SelfApproveNotAllowed";

    /// <summary>无建权限却调 Create（含改/删/提交草稿）。HTTP 403。</summary>
    public const string AdjustCreateDenied = Namespace + ":Adjust:CreateDenied";

    /// <summary>无 Finance.Adjust.Submit 却提交。HTTP 403。</summary>
    public const string AdjustSubmitDenied = Namespace + ":Adjust:SubmitDenied";

    /// <summary>无 Finance.Adjust.Approve 却审批。HTTP 403。</summary>
    public const string AdjustApproveDenied = Namespace + ":Adjust:ApproveDenied";

    /// <summary>无 Finance.Adjust.Reject 却驳回。HTTP 403。</summary>
    public const string AdjustRejectDenied = Namespace + ":Adjust:RejectDenied";

    /// <summary>无 Finance.Adjust.Reverse 却冲销。HTTP 403。</summary>
    public const string AdjustReverseDenied = Namespace + ":Adjust:ReverseDenied";

    /// <summary>当前状态不允许该操作（如非 Pending 批/驳、Pending 改）。HTTP 400。</summary>
    public const string AdjustInvalidStatus = Namespace + ":Adjust:InvalidStatus";

    /// <summary>驳回时必须填写原因。HTTP 400。</summary>
    public const string AdjustRejectReasonRequired = Namespace + ":Adjust:RejectReasonRequired";

    /// <summary>只有申请人能改/删/提交自己的调账。HTTP 403。</summary>
    public const string AdjustNotApplicant = Namespace + ":Adjust:NotApplicant";

    /// <summary>只有申请人能把 Rejected 重开为 Draft。HTTP 403。</summary>
    public const string AdjustReopenDenied = Namespace + ":Adjust:ReopenDenied";

    /// <summary>调账单不存在（含跨租户）。HTTP 404。</summary>
    public const string AdjustNotFound = Namespace + ":Adjust:NotFound";

    /// <summary>
    /// 调账输入非法。HTTP 400。data.Fields 为逗号分隔字段名。
    /// 建/改草稿：缺 amount/currency/debitAccountCode、金额≤0 等；
    /// 提交审批：reason 空/空白 → Fields=reason（草稿阶段 reason 可省略）。
    /// </summary>
    public const string AdjustInvalidInput = Namespace + ":Adjust:InvalidInput";

    /// <summary>对账区间非法（未带 Z/偏移，或 rangeStart ≥ rangeEnd）。HTTP 400。</summary>
    public const string ReconcileInvalidRange = Namespace + ":Reconcile:InvalidRange";

    /// <summary>幂等键已存在但内容冲突。HTTP 409。</summary>
    public const string IdempotencyConflict = Namespace + ":IdempotencyConflict";

    /// <summary>借贷不平衡，拒绝过账。HTTP 400。</summary>
    public const string UnbalancedVoucher = Namespace + ":UnbalancedVoucher";

    /// <summary>Ledger 总开关或 BizType 灰度关闭，拒绝业务 Post。HTTP 403。</summary>
    public const string LedgerDisabled = Namespace + ":LedgerDisabled";

    /// <summary>找不到过账规则模板。HTTP 400。</summary>
    public const string PostingRuleNotFound = Namespace + ":PostingRuleNotFound";

    /// <summary>事件字段非法（金额≤0、缺币种、超长等）。HTTP 400。</summary>
    public const string InvalidEvent = Namespace + ":InvalidEvent";

    /// <summary>模板要求 AgentId（佣金类）而事件未带。HTTP 400。</summary>
    public const string AgentIdRequired = Namespace + ":AgentIdRequired";

    /// <summary>科目不存在（含非法 ParentId / 父链成环时复用）。HTTP 400/404 视调用点。</summary>
    public const string AccountNotFound = Namespace + ":AccountNotFound";
    /// <summary>系统种子科目只读（不可改/删/启停）。HTTP 403。</summary>
    public const string AccountSystemReadOnly = Namespace + ":Account:SystemReadOnly";
    /// <summary>科目已有分录（或仍被调账单借贷引用）：不可删、不可改 code/type/direction。HTTP 400。</summary>
    public const string AccountHasEntries = Namespace + ":Account:HasEntries";
    /// <summary>科目编码重复。HTTP 400。</summary>
    public const string AccountCodeDuplicate = Namespace + ":Account:CodeDuplicate";
    /// <summary>仍有子科目，不可删除。HTTP 400。</summary>
    public const string AccountHasChildren = Namespace + ":Account:HasChildren";

    /// <summary>用户/代理子账户余额不足（非负规则）。HTTP 400。</summary>
    public const string InsufficientBalance = Namespace + ":InsufficientBalance";

    /// <summary>凭证不存在（含跨租户）。HTTP 404。</summary>
    public const string VoucherNotFound = Namespace + ":VoucherNotFound";

    /// <summary>红冲凭证本身不可再红冲。HTTP 400。</summary>
    public const string VoucherNotReversible = Namespace + ":VoucherNotReversible";

    /// <summary>Prepayment 已冻结新写入。HTTP 403。</summary>
    public const string PrepaymentWritesFrozen = Namespace + ":Prepayment:WritesFrozen";
}
