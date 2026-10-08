using System;
using System.Threading.Tasks;
using AbpAdmin.OperationLogs;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace AbpAdmin.Identity;

/// <summary>
/// <see cref="IIdentityRoleAppService"/> 的应用服务替换：删除角色前校验「角色下仍有用户则拒绝」。
/// ABP 原生删除（IdentityRoleManager.DeleteAsync）会静默清掉所有用户-角色关联，
/// 用户权限无感丢失且无任何提示；借鉴 Admin.NET SysRoleService.DeleteRole（D1025）——
/// 有人用的角色先改派再删，删除才是显式决定。级联清理（MenuGrant/RoleDataScope/PermissionGrants）
/// 不变，仍由各自的事件处理器负责。
/// <para>替换方式按 ABP 官方服务替换模式（同 AbpAdminSettingUiAppService 先例）：
/// [Dependency(ReplaceServices=true)] + [ExposeServices(接口+基类)]，Identity 模块的
/// 路由（/api/identity/roles）解析到本类。[RemoteService(false)] 防止本程序集的
/// 常规控制器把本类再暴露一份 /api/app/* 重复端点。</para>
/// <para>操作日志只能服务内手写（WriteDeleteLogOnCommit）：/api/identity/roles 走的是
/// Identity 模块自带的 HttpApi 控制器而非自动 API 控制器，[OperationLog] 特性挂在
/// 应用服务方法上透传不到 MVC Action 的 MethodInfo——与 SettingUi 同一情况同一解法。</para>
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IIdentityRoleAppService), typeof(IdentityRoleAppService))]
[Volo.Abp.RemoteService(false)]
public class AbpAdminRoleAppService : IdentityRoleAppService
{
    private readonly IIdentityUserRepository _userRepository;
    private readonly IOperationLogWriter _operationLogWriter;
    private readonly Volo.Abp.Tracing.ICorrelationIdProvider _correlationIdProvider;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AbpAdminRoleAppService(
        IdentityRoleManager roleManager,
        IIdentityRoleRepository roleRepository,
        IIdentityUserRepository userRepository,
        IOperationLogWriter operationLogWriter,
        Volo.Abp.Tracing.ICorrelationIdProvider correlationIdProvider,
        IHttpContextAccessor httpContextAccessor)
        : base(roleManager, roleRepository)
    {
        _userRepository = userRepository;
        _operationLogWriter = operationLogWriter;
        _correlationIdProvider = correlationIdProvider;
        _httpContextAccessor = httpContextAccessor;
        // IHttpContextAccessor 由宿主 ASP.NET Core / 测试基座（AbpAdminApplicationTestModule）注册；
        // HttpContext 在无请求上下文（后台/测试）时为 null，取值处一律可空处理
    }

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
        WriteDeleteLogOnCommit(role.Name!, id);
    }

    /// <summary>
    /// 删除成功后在 UoW 提交回调里落操作日志（fail-open：日志失败不影响已提交的删除），
    /// 形状对齐 OperationLogActionFilter 写出的记录（CorrelationId 与审计日志同源可 join）。
    /// </summary>
    private void WriteDeleteLogOnCommit(string roleName, Guid roleId)
    {
        var uow = UnitOfWorkManager.Current;
        var entry = new OperationLogEntry
        {
            Type = "身份管理",
            SubType = "删除角色",
            BizId = roleId.ToString(),
            Action = $"删除了角色 {roleName}",
            Success = true,
            RequestMethod = "DELETE",
            RequestUrl = $"/api/identity/roles/{roleId}",
            CorrelationId = _correlationIdProvider.Get(),
            ClientIpAddress = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
            UserAgent = _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.ToString(),
        };

        if (uow == null)
        {
            _ = _operationLogWriter.WriteAsync(entry);
            return;
        }

        uow.OnCompleted(async () =>
        {
            try
            {
                await _operationLogWriter.WriteAsync(entry);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "删除角色操作日志写入失败（RoleName={RoleName}）", roleName);
            }
        });
    }
}
