using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Account.Web.Pages.Account;

namespace AbpAdmin.Pages.Account;

/// <summary>
/// T4.2：已登录用户绑定外部登录。需要 Host 域上的 Identity cookie（OIDC 登录后通常仍在）。
/// 只接受 POST Challenge：Identity cookie 为 SameSite=Lax，跨站 GET 会带 cookie，跨站 POST 不会。
/// 回调与失败跳转一律 <see cref="AccountPageModel.RedirectSafelyAsync"/>，ReturnUrl 必须在 App:RedirectAllowedUrls 内。
/// Provider 只允许已接入的方案名，拒绝客户端任意 scheme 以免开放重定向到未注册的中间人。
/// </summary>
[Authorize]
public class LinkLoginModel : AccountPageModel
{
    private static readonly HashSet<string> AllowedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "GitHub",
        "Microsoft",
        "Weixin",
        "Google"
    };

    [BindProperty]
    public string? Provider { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// GET 不再 Challenge。Lax cookie 会随跨站顶层 GET 发出，第三方页面可以诱导绑定。
    /// </summary>
    public virtual async Task<IActionResult> OnGetAsync()
    {
        return await RedirectSafelyAsync(ReturnUrl ?? "~/");
    }

    public virtual IActionResult OnPost()
    {
        if (string.IsNullOrWhiteSpace(Provider) || !AllowedProviders.Contains(Provider))
        {
            return LocalRedirect("~/");
        }

        var redirectUrl = Url.Page("./LinkLogin", pageHandler: "Callback", values: new { ReturnUrl });
        var properties = SignInManager.ConfigureExternalAuthenticationProperties(Provider, redirectUrl);
        return Challenge(properties, Provider);
    }

    public virtual async Task<IActionResult> OnGetCallbackAsync()
    {
        var info = await SignInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            return await RedirectSafelyAsync(ReturnUrl ?? "~/");
        }

        var user = await UserManager.GetUserAsync(User);
        if (user == null)
        {
            return Challenge();
        }

        var result = await UserManager.AddLoginAsync(user, info);
        if (!result.Succeeded && result.Errors.All(e => e.Code != "LoginAlreadyAssociated"))
        {
            Alerts.Danger(string.Join("; ", result.Errors.Select(e => e.Description)));
        }

        return await RedirectSafelyAsync(ReturnUrl ?? "~/");
    }
}
