using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Identity;

/// <summary>
/// 角色侧的用户只读查询（GET /api/app/role-user-admin?roleId=...）。
/// 角色的分派/改派仍在用户管理页（用户编辑表单选角色）；这里只回答
/// 「谁拥有这个角色」——排查权限问题时的反向视角（Admin.NET 角色页没有此能力，
/// ABP 商业版有；仓储 IIdentityUserRepository.GetListAsync/GetCountAsync 原生支持 roleId 过滤）。
/// </summary>
public interface IRoleUserAdminAppService : IApplicationService
{
    Task<PagedResultDto<RoleUserDto>> GetListAsync(Guid roleId, PagedAndSortedResultRequestDto input);
}
