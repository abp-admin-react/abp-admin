using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Authorization;

namespace AbpAdmin.Authorization;

/// <summary>
/// 给 ABP OpenIddict 模块的 connect 协议端点补 <see cref="AllowAnonymousFilter"/>。
/// 问题：上游 <c>Volo.Abp.OpenIddict.Controllers.TokenController</c> / <c>AuthorizeController</c> /
/// <c>LogoutController</c>（end-session，源文件名 EndSessionController.cs，10.6.1 实测类名是
/// LogoutController——按类 FullName 匹配，别看文件名）类上既没有 [Authorize] 也没有
/// [AllowAnonymous]（ABP 10.6.1 程序集反编译核实），在宿主启用
/// <c>AuthorizationOptions.FallbackPolicy</c>（默认拒绝）后，/connect/token（登录本身）、
/// /connect/authorize、/connect/endsession（SPA 登出走的就是它，见 web/src/abp/oidc.ts
/// 的 end_session_endpoint）会被要求已认证——登录/登出流程直接死锁。
/// 这里按控制器类型精确豁免三个协议端点；<c>UserInfoController</c>（connect/userinfo）自带
/// [Authorize(OpenIddictServer scheme)]，不在豁免之列。
/// 豁免会体现在端点元数据上，由 AnonymousEndpointSweepTests 双向兜住：
/// 正向——新增任何 [AllowAnonymous] 端点不在白名单即红；
/// 反向——connect/token|authorize|endsession 必须真的携带匿名元数据（本约定因 ABP 升级
/// 改名而失配时，反向断言先红，而不是等登录炸了才发现）。
/// </summary>
public class OpenIddictConnectEndpointsConvention : IControllerModelConvention
{
    private static readonly string[] AnonymousControllerTypeNames =
    [
        "Volo.Abp.OpenIddict.Controllers.TokenController",
        "Volo.Abp.OpenIddict.Controllers.AuthorizeController",
        "Volo.Abp.OpenIddict.Controllers.LogoutController",
    ];

    public void Apply(ControllerModel controller)
    {
        if (Array.IndexOf(AnonymousControllerTypeNames, controller.ControllerType.FullName) < 0)
        {
            return;
        }

        // 不用框架 AllowAnonymousFilter：它不实现 IAllowAnonymous，
        // AuthorizationMiddleware（FallbackPolicy 生效处）不认——见 EndpointAllowAnonymousMarker
        controller.Filters.Add(new EndpointAllowAnonymousMarker());
    }
}
