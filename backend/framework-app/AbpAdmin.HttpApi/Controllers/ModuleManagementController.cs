using System.Threading.Tasks;
using AbpAdmin.ClientErrors;
using AbpAdmin.Modules;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AbpAdmin.Controllers;

/// <summary>管理本部署已装配模块的新业务受理状态。</summary>
[Route("api/admin/module-management")]
[Authorize(AbpAdminPermissions.ModuleManagement.Default)]
[ClientErrorAudience(ClientErrorAudience.AdminCompatibility)]
public class ModuleManagementController(IModuleManagementAppService service) : AbpAdminController
{
    /// <summary>读取部署模块状态和版本。</summary>
    [HttpGet]
    public Task<ModuleStateListDto> GetAsync() => service.GetAsync();

    /// <summary>预览启用依赖或停用阻止原因。</summary>
    [HttpPost("preview")]
    [Authorize(AbpAdminPermissions.ModuleManagement.Update)]
    public Task<ModuleChangePreviewDto> PreviewAsync([FromBody] ModuleChangeInput input) => service.PreviewAsync(input);

    /// <summary>按预期版本原子提交启停及启用依赖。</summary>
    [HttpPut]
    [Authorize(AbpAdminPermissions.ModuleManagement.Update)]
    public Task<ModuleStateListDto> UpdateAsync([FromBody] ModuleChangeInput input) => service.UpdateAsync(input);
}
