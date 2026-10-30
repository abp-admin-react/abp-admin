namespace AbpAdmin;

/// <summary>
/// 身份声明写入不得携带权威类型的错误码。单独成文件（避免追加进既有文件时被增量构建跳过）。
/// </summary>
public static class AbpAdminIdentityClaimsErrorCodes
{
    public const string CannotCarryRole = "AbpAdmin:IdentityClaimsCannotCarryRole";
}
