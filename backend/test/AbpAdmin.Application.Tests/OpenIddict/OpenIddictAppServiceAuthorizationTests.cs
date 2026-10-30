using System;
using System.Linq;
using System.Reflection;
using AbpAdmin.OpenIddict;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Shouldly;
using Xunit;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// 回归锁：OpenIddict 客户端 Create/Update 全量重写权限面（scopes/grant types/flows/
/// PKCE 要求清空重填），必须同时要求 ManagePermissions——该授权位不接线即为死代码，
/// 任何持 Applications.Update 的主体都能把机密客户端改成 password 流、
/// 剥掉 PKCE。本锁以元数据断言钉死接线（与匿名端点全量审计同一哲学：行为由源码钉死时，
/// 断言元数据 = 断言行为）；同时钉死 UpdateTokenLifetimeAsync 不需要该位（仅覆盖 lifetime
/// settings，不碰权限面，误加会破坏纯生命周期管理角色）。
/// </summary>
public class OpenIddictAppServiceAuthorizationTests
{
    private static AuthorizeAttribute[] AuthorizeOf(string methodName)
    {
        var method = typeof(OpenIddictApplicationAppService).GetMethod(
            methodName,
            BindingFlags.Public | BindingFlags.Instance,
            binder: null,
            types: methodName == nameof(OpenIddictApplicationAppService.UpdateAsync)
                ? new[] { typeof(Guid), typeof(UpdateOpenIddictApplicationDto) }
                : methodName == nameof(OpenIddictApplicationAppService.CreateAsync)
                    ? new[] { typeof(CreateOpenIddictApplicationDto) }
                    : new[] { typeof(Guid), typeof(UpdateOpenIddictApplicationTokenLifetimeDto) },
            modifiers: null);
        method.ShouldNotBeNull();
        return method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToArray();
    }

    [Fact]
    public void Create_And_Update_Must_Require_ManagePermissions()
    {
        foreach (var name in new[] { nameof(OpenIddictApplicationAppService.CreateAsync), nameof(OpenIddictApplicationAppService.UpdateAsync) })
        {
            var policies = AuthorizeOf(name).Select(a => a.Policy).ToList();
            policies.ShouldContain(AbpAdminPermissions.OpenIddict.Applications.ManagePermissions,
                $"{name} 必须接上 ManagePermissions 门（客户端权限面改写授权）");
        }
    }

    [Fact]
    public void UpdateTokenLifetime_Must_Not_Require_ManagePermissions()
    {
        var policies = AuthorizeOf(nameof(OpenIddictApplicationAppService.UpdateTokenLifetimeAsync))
            .Select(a => a.Policy).ToList();
        policies.ShouldNotContain(AbpAdminPermissions.OpenIddict.Applications.ManagePermissions,
            "token lifetime 覆盖不碰权限面，保持纯生命周期管理授权");
    }
}
