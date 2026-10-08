using System;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

/// <summary>
/// <see cref="IIdentityRoleAppService"/> 的应用服务替换：删除角色前校验「角色下仍有用户则拒绝」。
/// ABP 原生删除（IdentityRoleManager.DeleteAsync）会静默清掉所有用户-角色关联，
/// 用户权限无感丢失且无任何提示；借鉴 Admin.NET SysRoleService.DeleteRole（D1025）——
/// 有人用的角色先改派再删，删除才是显式决定。级联清理（MenuGrant/RoleDataScope/PermissionGrants）
/// 不变，仍由各自的事件处理器负责。
/// <para>替换方式按 ABP 官方服务替换模式（同 AbpAdminSettingUiAppService 先例）：
/// [Dependency(ReplaceServices=true)] + [ExposeServices(接口+基类)]，Identity 模块的
/// 常规控制器（/api/identity/roles）解析到本类。[RemoteService(false)] 防止本程序集的
/// 常规控制器把本类再暴露一份 /api/app/* 重复端点。</para>
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IIdentityRoleAppService), typeof(IdentityRoleAppService))]
[Volo.Abp.RemoteService(false)]
public class AbpAdminRoleAppService : IdentityRoleAppService
{
    private readonly IIdentityUserRepository _userRepository;

    public AbpAdminRoleAppService(
        IdentityRoleManager roleManager,
        IIdentityRoleRepository roleRepository,
        IIdentityUserRepository userRepository)
        : base(roleManager, roleRepository)
    {
        _userRepository = userRepository;
    }

    [OperationLog("身份管理", "删除角色", BizNo = "{{id}}", Success = "删除了角色 {{role(id)}}")]
    public override async Task DeleteAsync(Guid id)
    {
        // 对齐基类幂等语义：角色不存在直接返回（不抛 404）
        var role = await RoleRepository.FindAsync(id);
        if (role == null)
        {
            return;
        }

        var userIds = await _userRepository.GetUserIdListByRoleIdAsync(id);
        if (userIds.Count > 0)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Identity.RoleHasUsers)
                .WithData("RoleName", role.Name)
                .WithData("Count", userIds.Count);
        }

        await base.DeleteAsync(id);
    }
}
