namespace AbpAdmin.DataScopes;

/// <summary>
/// 角色数据范围字段长度常量（表 AppRoleDataScopes）。实体校验与输入 DTO 共用同一份，避免魔法数漂移。
/// </summary>
public static class RoleDataScopeConsts
{
    /// <summary>角色名最大长度（对应 IdentityRole.Name；ABP Identity 的 RoleConsts.MaxNameLength = 256）。</summary>
    public const int MaxRoleNameLength = 256;
}
