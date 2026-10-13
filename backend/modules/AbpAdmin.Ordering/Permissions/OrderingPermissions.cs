namespace AbpAdmin.Ordering.Permissions;

/// <summary>
/// 订单模块权限名（注册名与 MenuTemplateDefinition / 前端 access.ts 一致，稳定不变）。
/// </summary>
public static class OrderingPermissions
{
    public const string GroupName = "AbpAdmin.Ordering";

    public static class Orders
    {
        public const string Default = GroupName + ".Orders";

        /// <summary>
        /// 查看全部用户的订单（管理端列表/明细）。与菜单可见性（Default）分键：
        /// 黑客轮实证：Default 同时驱动菜单与 order-admin API 时，拿到菜单权限的普通用户
        /// 能经 API 读到跨用户的全部订单（含他人 userId/金额/异常明细）——最小权限破坏。
        /// </summary>
        public const string ViewAll = GroupName + ".Orders.ViewAll";

        /// <summary>管理端把业务单从 Paid 标成 Fulfilled（不可回冲）。</summary>
        public const string Fulfill = GroupName + ".Orders.Fulfill";

        /// <summary>人工退款记录（追加异常明细，不改订单状态）。</summary>
        public const string RecordManualRefund = GroupName + ".Orders.RecordManualRefund";
    }
}
