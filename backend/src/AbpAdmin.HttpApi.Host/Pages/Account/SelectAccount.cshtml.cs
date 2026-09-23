using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.Account.Web.Pages.Account;
using Volo.Abp.Identity;

namespace AbpAdmin.Pages.Account;

/// <summary>
/// 账号选择页（prompt=select_account）。ABP AuthorizeController 收到该 prompt 时
/// 跳到本页（RedirectUri=授权端点完整地址，TempData 已写 IgnoreSelectAccount 防回环）。
/// 两个动作：继续用当前账号 → 直接回授权端点；换账号 → 登出当前会话后回授权端点（会落到登录表单）。
/// </summary>
[Authorize]
public class SelectAccountModel : AccountPageModel
{
    /// <summary>授权端点完整地址（/connect/authorize?...），仅接受站内授权端点，防开放跳转。</summary>
    [BindProperty(SupportsGet = true)]
    public string? RedirectUri { get; set; }

    public string? CurrentUserName { get; private set; }

    public string? CurrentUserEmail { get; private set; }

    public virtual async Task<IActionResult> OnGetAsync()
    {
        if (!IsSafeRedirectUri())
        {
            return Redirect("~/");
        }

        var user = await UserManager.GetUserAsync(User);
        if (user == null)
        {
            // 会话已失效：直接回授权端点，会正常走到登录页
            return Redirect(RedirectUri!);
        }

        CurrentUserName = user.UserName;
        CurrentUserEmail = user.Email;
        return Page();
    }

    public virtual async Task<IActionResult> OnPostAsync(string action)
    {
        if (!IsSafeRedirectUri())
        {
            return Redirect("~/");
        }

        if (action == "Switch")
        {
            // 登出当前应用会话后回授权端点：未认证 → 登录表单，换号后继续授权
            await SignInManager.SignOutAsync();
        }

        return Redirect(RedirectUri!);
    }

    /// <summary>
    /// 开放跳转守卫：目标必须是本站 /connect/authorize 开头的路径。
    /// 三重检查（前缀、禁协议、禁协议相对）封死外域跳转；编码字符在 Location 中保持编码、
    /// CRLF 被 Kestrel 头校验拦截，均无法绕过。查询串里的 redirect_uri 由 OpenIddict
    /// 按客户端注册的 URI 再校验，不构成本页责任。
    /// </summary>
    private bool IsSafeRedirectUri()
    {
        if (string.IsNullOrWhiteSpace(RedirectUri))
        {
            return false;
        }

        return RedirectUri.StartsWith("/connect/authorize", StringComparison.OrdinalIgnoreCase)
               && !RedirectUri.Contains("://", StringComparison.Ordinal)
               && !RedirectUri.StartsWith("//", StringComparison.Ordinal);
    }
}
