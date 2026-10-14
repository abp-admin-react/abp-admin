namespace AbpAdmin.Ledger.Permissions;

/// <summary>财务权限码（定稿 §5.2）。字符串必须与前端菜单/按钮绑定一致。</summary>
public static class FinancePermissions
{
    public const string GroupName = "Finance";

    public static class Ledger
    {
        public const string Default = GroupName + ".Ledger";
        public const string View = Default + ".View";
        public const string Export = Default + ".Export";
    }

    public static class Account
    {
        public const string Default = GroupName + ".Account";
        public const string Manage = Default + ".Manage";
    }

    public static class Rule
    {
        public const string Default = GroupName + ".Rule";
        public const string View = Default + ".View";
    }

    public static class Voucher
    {
        public const string Default = GroupName + ".Voucher";
        public const string View = Default + ".View";
        public const string Reverse = Default + ".Reverse";
    }

    public static class Adjust
    {
        public const string Default = GroupName + ".Adjust";
        public const string Create = Default + ".Create";
        public const string Submit = Default + ".Submit";
        public const string Approve = Default + ".Approve";
        public const string Reject = Default + ".Reject";
        public const string Reverse = Default + ".Reverse";

        /// <summary>审批/驳回 applicantId==当前用户 的单据时额外要求；默认不授予任何角色。</summary>
        public const string ApproveOwn = Default + ".ApproveOwn";
    }

    public static class Reconcile
    {
        public const string Default = GroupName + ".Reconcile";
        public const string Run = Default + ".Run";
    }

    public static class Period
    {
        public const string Default = GroupName + ".Period";
        public const string View = Default + ".View";
    }

    public static class BizDoc
    {
        public const string Default = GroupName + ".BizDoc";
        public const string View = Default + ".View";
    }

    public static class Balance
    {
        public const string Default = GroupName + ".Balance";
        public const string View = Default + ".View";
    }

    /// <summary>代理强制按自己 AgentId 过滤 A6/A7。</summary>
    public static class Scope
    {
        public const string Agent = GroupName + ".Scope.Agent";
    }
}
