namespace AbpAdmin.Collection.Permissions;

/// <summary>收款模块权限名（注册名与 MenuTemplateDefinition / 前端 access.ts 一致）。</summary>
public static class CollectionPermissions
{
    public const string GroupName = "AbpAdmin.Collection";

    public static class Collections
    {
        public const string Default = GroupName + ".Collections";
    }

    /// <summary>链网运维（RPC/助记词/测量上限/金库地址/最低充值额）——高危动作独立权限。</summary>
    public static class Network
    {
        public const string Admin = GroupName + ".Network.Admin";
    }

    /// <summary>提现管理（审核/拒绝/退款/开账回填）——资金出账动作，与链网运维同级独立权限。</summary>
    public static class Withdraw
    {
        public const string Admin = GroupName + ".WithdrawAdmin";
    }
}
