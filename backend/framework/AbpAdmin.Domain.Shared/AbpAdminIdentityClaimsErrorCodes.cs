namespace AbpAdmin;

/// <summary>
/// 安全审计 run-1（TV-03）新增错误码：身份声明写入不得携带权威类型。单独成文件——
/// 同文件追加曾因本机增量构建陈旧缓存对下游不可见（见修复说明），新文件强制入编译。
/// </summary>
public static class AbpAdminIdentityClaimsErrorCodes
{
    public const string CannotCarryRole = "AbpAdmin:IdentityClaimsCannotCarryRole";
}
