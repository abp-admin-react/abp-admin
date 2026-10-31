using System;
using System.Collections.Generic;
using System.Security.Claims;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Security.Claims;
using Xunit;

namespace AbpAdmin.Identity;

/// <summary>
/// 声明权威类型守卫单测（安全审计 run-2 C-1 回归锁）：
/// 用户声明与角色声明两条写面共用的拒收口径——canonical 长 URI + ClaimsMap 源别名
/// role/roles（大小写不敏感）；空白类型与普通类型放行。
/// </summary>
public class IdentityClaimAuthorityGuardTests
{
    private static IEnumerable<(string?, string?)> Claims(params (string?, string?)[] claims) => claims;

    [Fact]
    public void Canonical_Role_Claim_Type_Is_Rejected()
    {
        // 运行时 AbpClaimTypes.Role 默认 = ClaimTypes.Role 长 URI——run-1 修复只挡它
        IdentityClaimAuthorityGuard.ContainsAuthoritativeClaimType(
            Claims((ClaimTypes.Role, "admin"))).ShouldBeTrue();
    }

    [Theory]
    [InlineData("role")]
    [InlineData("Role")]
    [InlineData("ROLES")]
    [InlineData("roles")]
    public void Short_Aliases_Are_Rejected_Case_Insensitively(string type)
    {
        // ClaimsMap 源别名：经 OpenIddict claims 映射同样能进入 role claims——两条写面都必须挡
        IdentityClaimAuthorityGuard.ContainsAuthoritativeClaimType(
            Claims((type, "admin"))).ShouldBeTrue();
    }

    [Fact]
    public void Non_Authoritative_Types_Pass()
    {
        IdentityClaimAuthorityGuard.ContainsAuthoritativeClaimType(
            Claims(("department", "rd"), ("tenant_id", "t1"), ("x", "y"))).ShouldBeFalse();
    }

    [Fact]
    public void Blank_Types_Pass_Guard()
    {
        // 与两条写面既有的空白过滤口径一致：空白不算违规（后续写入前会被过滤掉）
        IdentityClaimAuthorityGuard.ContainsAuthoritativeClaimType(
            Claims((null, "v"), ("", "v"), ("  ", "v"))).ShouldBeFalse();
    }

    [Fact]
    public void Mixed_List_Is_Rejected_When_Any_Authoritative_Type_Present()
    {
        IdentityClaimAuthorityGuard.ContainsAuthoritativeClaimType(
            Claims(("department", "rd"), ("role", "admin"), ("x", "y"))).ShouldBeTrue();
    }

    [Fact]
    public void Ensure_Throws_Business_Exception_With_Claim_Error_Code()
    {
        var ex = Should.Throw<BusinessException>(() =>
            IdentityClaimAuthorityGuard.EnsureNoAuthoritativeClaimTypes(
                Claims((ClaimTypes.Role, "admin"))));
        ex.Code.ShouldBe(AbpAdminIdentityClaimsErrorCodes.CannotCarryRole);
    }

    [Fact]
    public void Ensure_Passes_For_Allowed_Types()
    {
        IdentityClaimAuthorityGuard.EnsureNoAuthoritativeClaimTypes(
            Claims(("department", "rd")));
    }
}
