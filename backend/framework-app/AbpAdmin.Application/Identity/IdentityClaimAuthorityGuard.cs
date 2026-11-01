using System;
using System.Collections.Generic;
using System.Security.Claims;
using Volo.Abp;
using Volo.Abp.Security.Claims;

namespace AbpAdmin.Identity;

/// <summary>
/// 声明写入的权威类型守卫：用户声明与角色声明两条写面共用的拒收口径。
/// ABP 动态声明默认清单含 Role（<see cref="AbpClaimTypes.Role"/>），而角色记录声明经
/// UserClaimsPrincipalFactory 进入全体成员 principal 的 role claims——无论写在用户还是
/// 角色上，{role:'admin'} 都会让 CurrentUser.Roles 命中数据范围的 admin 豁免（IsAll 行可见 +
/// RoleDataScope 反提权守卫豁免），等价于绕开角色管理权限的自我提权。
/// 拒收清单（全部大小写不敏感）：
/// ① 运行时 <see cref="AbpClaimTypes.Role"/>（宿主 ClaimsMap 初始化后可能是短名 "role"）；
/// ② Microsoft 规范长 URI <see cref="ClaimTypes.Role"/>（sectest 黑盒验证：宿主把 ① 映射成
///    短名后，长 URI 曾从只比 ① 的守卫下逃逸并落库）；
/// ③ ClaimsMap 源别名 "role"/"roles"。
/// 空值/空白类型不算违规（与两条写面既有的空白过滤口径一致）。
/// </summary>
public static class IdentityClaimAuthorityGuard
{
    private static readonly string[] AliasClaimTypes = { "role", "roles", ClaimTypes.Role };

    /// <summary>claims 里是否存在权威类型；命中即应抛 <see cref="BusinessException"/>。</summary>
    public static bool ContainsAuthoritativeClaimType(IEnumerable<(string? ClaimType, string? ClaimValue)> claims)
    {
        var runtimeRole = AbpClaimTypes.Role;
        foreach (var (type, _) in claims)
        {
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            if (type.Equals(runtimeRole, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var alias in AliasClaimTypes)
            {
                if (type.Equals(alias, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>命中权威类型即抛错（两条写面的统一入口）。</summary>
    public static void EnsureNoAuthoritativeClaimTypes(IEnumerable<(string? ClaimType, string? ClaimValue)> claims)
    {
        if (ContainsAuthoritativeClaimType(claims))
        {
            throw new BusinessException(AbpAdminIdentityClaimsErrorCodes.CannotCarryRole);
        }
    }
}
