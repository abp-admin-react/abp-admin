namespace AbpAdmin.TokenGateway;

/// <summary>
/// TokenGateway 权限位。管理面此前绑死 "admin" 角色（字符串硬编码），无法给运营角色
/// 单独授予/在权限页审计回收；改为权限位后 ABP 默认仍把全部权限授给 admin 角色，
/// 运营角色可在权限管理页按位授予（OCR design 修复）。
/// </summary>
public static class TokenGatewayPermissions
{
    public const string GroupName = "TokenGateway";

    public static class Plans
    {
        public const string Default = GroupName + ".Plans";
        public const string Manage = Default + ".Manage";
    }
}
