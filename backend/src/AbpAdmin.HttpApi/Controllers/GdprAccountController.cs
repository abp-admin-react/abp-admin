using System.Threading.Tasks;
using AbpAdmin.Gdpr;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc;

namespace AbpAdmin.Controllers;

/// <summary>
/// GDPR 账户删除控制器。
/// 应用服务只负责业务逻辑，HTTP 动词由此 Controller 钉死为 DELETE，
/// 避免 ABP 动态 API 对 DELETE 请求 body 绑定的问题。
/// </summary>
[Route("api/app/gdpr-request")]
[Authorize]
public class GdprAccountController : AbpController
{
    private readonly IGdprRequestAppService _gdprRequestAppService;

    public GdprAccountController(IGdprRequestAppService gdprRequestAppService)
    {
        _gdprRequestAppService = gdprRequestAppService;
    }

    /// <summary>
    /// 删除当前用户账户（需密码确认）。
    /// 先匿名化再删除，个人数据请求记录一并清除。
    /// </summary>
    [HttpDelete("current-user-account")]
    public async Task<IActionResult> DeleteCurrentUserAccountAsync([FromBody] DeleteAccountInput input)
    {
        await _gdprRequestAppService.DeleteCurrentUserAccountAsync(input);
        return NoContent();
    }
}
