using AbpAdmin.TokenGateway.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AbpAdmin.TokenGateway.Http;

/// <summary>价表管理（走宿主正常登录态，权限位 TokenGateway.Plans.Manage）。改代币数/标价/上下架，不发版。
/// 此前绑死 "admin" 角色字符串，无法给运营角色单独授予/回收；ABP 默认把全部权限授给 admin，
/// 迁移到权限位后 admin 不受影响，运营角色可在权限管理页按位授予。</summary>
[ApiController]
[Authorize(TokenGatewayPermissions.Plans.Manage)]
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
