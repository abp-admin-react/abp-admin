using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AbpAdmin.Authorization;

/// <summary>
/// 给登录链路的 Account Razor Pages 补匿名元数据。
/// 问题：ABP 10.6.1 的 Account 模块页面模型（LoginModel/ForgotPasswordModel/RegisterModel/
/// LockoutModel…）与本宿主替换的页面模型（AbpAdminLoginModel/TwoFactorVerificationModel/
/// LinkLoginModel）都既没有 [Authorize] 也没有 [AllowAnonymous]，模块只给 /Account/Manage
/// 注册过 AuthorizePage——其余页面在宿主 FallbackPolicy（默认拒绝）下会被要求已认证，
/// 匿名访客打不开 /Account/Login，OpenIddict 授权码流程（重定向到登录页）与找回密码页全断。
/// 按页面相对路径精确豁免"登录之前就必须可达"的页面集合；/Account/Manage 等
/// 已有显式授权的页面绝不入列——IAllowAnonymous 会短路一切授权元数据，误加即漏洞。
/// 同时实现 IPageRouteModelConvention（selector 端点元数据出口）与
/// IPageApplicationModelConvention（Filters 出口）：不依赖 Razor 内部哪一层
/// 负责把标记带进端点表，两个出口都挂。豁免集合由 AnonymousEndpointSweepTests
/// 双向兜住（正向登记、反向 Account/Login 必达）。
/// </summary>
public class PreLoginRazorPagesConvention : IPageRouteModelConvention, IPageApplicationModelConvention
{
    /// <summary>
    /// 登录链路页面（ViewEnginePath 去前导斜杠）。
    /// 新增条目必须同步 AnonymousEndpointSweepTests 的 PreLoginPages 清单。
    /// </summary>
    internal static readonly string[] AnonymousPageRoutes =
    [
        "Account/Login",
        "Account/LoginWith2fa",
        "Account/LoginWithRecoveryCode",
        "Account/TwoFactorVerification",
        "Account/Lockout",
        "Account/ForgotPassword",
        "Account/ForgotPasswordConfirmation",
        "Account/Register",
        "Account/RegisterConfirmation",
        "Account/ConfirmEmail",
        "Account/LinkLogin",
        "Account/LinkLoginCallback",
        "Account/Logout",
        "Account/LoggedOut",
        "Account/AccessDenied",
    ];

    public void Apply(PageRouteModel routeModel)
    {
        var route = routeModel.ViewEnginePath.TrimStart('/');
        if (Array.IndexOf(AnonymousPageRoutes, route) < 0)
        {
            return;
        }

        foreach (var selector in routeModel.Selectors)
        {
            selector.EndpointMetadata.Add(new EndpointAllowAnonymousMarker());
        }
    }

    public void Apply(PageApplicationModel page)
    {
        var route = page.ViewEnginePath.TrimStart('/');
        if (Array.IndexOf(AnonymousPageRoutes, route) < 0)
        {
            return;
        }

        page.Filters.Add(new EndpointAllowAnonymousMarker());
    }
}
