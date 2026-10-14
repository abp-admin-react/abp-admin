using AbpAdmin.TokenGateway.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AbpAdmin.TokenGateway.Http;

/// <summary>价表管理（走宿主正常登录态，仅 admin 角色）。改代币数/标价/上下架，不发版。</summary>
[ApiController]
[Authorize(Roles = TokenGatewayConsts.AdminRole)]
[Route("api/token-gateway/admin/plans")]
[ApiExplorerSettings(GroupName = "token-gateway")]
public class AdminPlansController(PlanCatalog plans) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List() => Ok(new { items = await plans.AllAsync(includeInactive: true) });

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] PlanPatch patch)
    {
        try
        {
            return Ok(await plans.UpdateAsync(id, patch));
        }
        catch (GatewayException e)
        {
            return GatewayControllerBase.Error(e);
        }
    }
}
