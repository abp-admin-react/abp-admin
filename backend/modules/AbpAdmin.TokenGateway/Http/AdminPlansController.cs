using AbpAdmin.TokenGateway.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.TokenGateway.Http;

/// <summary>价表管理（走宿主正常登录态，权限位 TokenGateway.Plans.Manage）。改代币数/标价/上下架，不发版。
/// 权限不绑死 "admin" 角色字符串，可给运营角色单独授予/回收；ABP 默认把全部权限授给 admin，
/// 迁移到权限位后 admin 不受影响，运营角色可在权限管理页按位授予。</summary>
[ApiController]
[Authorize(TokenGatewayPermissions.Plans.Manage)]
[Route("api/token-gateway/admin/plans")]
[ApiExplorerSettings(GroupName = "token-gateway")]
public class AdminPlansController(PlanCatalog plans, ICurrentTenant currentTenant) : ControllerBase
{
    /// <summary>
    /// Host 专属资源的侧界守卫（对齐 AbpAdminAppService.EnsureHostSide 双保险口径）。TgPlans 是
    /// 无租户列的全局表，权限位锁 MultiTenancySides.Host（两侧可授时，
    /// 两侧可授，建租户种子会把缺省侧权限授给租户 admin——已由反编译 Volo.Abp.PermissionManagement
    /// 10.7.0 实证）——但存量库的租户授权行不会因定义收窄而消失，控制器侧显式拒绝兜底存量误授。
    /// </summary>
    private void EnsureHostSide()
    {
        if (currentTenant.Id != null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.HostSideOnly);
        }
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        EnsureHostSide();
        return Ok(new { items = await plans.AllAsync(includeInactive: true) });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] PlanPatch patch)
    {
        EnsureHostSide();
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
