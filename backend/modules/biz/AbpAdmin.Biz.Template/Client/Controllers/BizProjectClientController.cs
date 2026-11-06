using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Biz.Template.Client.Dtos;
using AbpAdmin.Biz.Template.ClientErrors;
using AbpAdmin.Biz.Template.Entities;
using AbpAdmin.Biz.Template.Permissions;
using AbpAdmin.Biz.Template.Services;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace AbpAdmin.Biz.Template.Client.Controllers;

/// <summary>
/// 客户错误协议接入样例入口。不是多租户客户授权模板，不能按此直接上线正式客户模块。
/// 默认不注册；需登录且持有 <see cref="BizTemplatePermissions.ClientErrorSample.Default"/>。
/// </summary>
[Route("api/client/biz-template")]
[Authorize(BizTemplatePermissions.ClientErrorSample.Default)]
[ClientErrorAudience(ClientErrorAudience.Public)]
public class BizProjectClientController : AbpController
{
    private readonly IBizProjectAccessService _bizProjectAccessService;

    public BizProjectClientController(IBizProjectAccessService bizProjectAccessService)
    {
        _bizProjectAccessService = bizProjectAccessService;
    }

    /// <summary>
    /// 当前用户自己的项目。未登录返回空数组，不 401（授权特性已要求登录；此处防 Id 空）。
    /// </summary>
    [HttpGet("projects")]
    public async Task<List<BizProjectClientDto>> GetProjectsAsync()
    {
        if (CurrentUser.Id is null)
        {
            return [];
        }

        var projects = await _bizProjectAccessService.GetOwnedListAsync(CurrentUser.Id.Value);
        return projects.Select(ToClientDto).ToList();
    }

    /// <summary>
    /// 单条客户项目。无权或不存在走 ProjectUnavailable 公开码，不泄露内部原因。
    /// </summary>
    [HttpGet("projects/{id}")]
    public async Task<BizProjectClientDto> GetProjectAsync(Guid id)
    {
        if (CurrentUser.Id is null)
        {
            throw new BusinessException(BizTemplateClientErrorCodes.ProjectUnavailable);
        }

        var project = await _bizProjectAccessService.GetOwnedActiveAsync(id, CurrentUser.Id.Value);
        return ToClientDto(project);
    }

    private static BizProjectClientDto ToClientDto(BizProject project)
    {
        return new BizProjectClientDto
        {
            Id = project.Id,
            Name = project.Name,
            Description = project.Description,
        };
    }
}
