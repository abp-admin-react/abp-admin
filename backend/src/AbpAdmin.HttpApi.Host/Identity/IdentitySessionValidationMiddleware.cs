using System.Threading.Tasks;
using AbpAdmin.Identity;
using Microsoft.AspNetCore.Http;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using Volo.Abp.Users;

namespace AbpAdmin.Identity;

/// <summary>
/// T4.1：令牌带 session_id 但库里已被撤销时返回 401，配合动态声明踢下线。
/// 没有 session_id 的令牌（客户端凭证、旧票、非用户授权）不拦——那不是本会话模型的票。
/// 命中后 TouchIfStaleAsync：清理 worker 看 LastAccessed，不续期会把活跃用户踢掉。
///
/// 中间件位置（UseUnitOfWork 之后）没有环境 UoW——UseUnitOfWork 走 Reserve()，
/// AmbientUnitOfWork.GetCurrentByChecking() 会跳过 IsReserved 的 UoW，Current 为 null，
/// 仓储查库会抛 "A DbContext can only be created inside a unit of work!"（见 DataScopeMiddleware
/// 同位置的处理）。必须自己开 requiresNew 的非事务 UoW 包住查询与续期写，
/// 不能依赖 DomainService 约定拦截碰巧兜底（拦截是否生效取决于解析形态，属脆弱耦合）。
/// </summary>
public class IdentitySessionValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public IdentitySessionValidationMiddleware(
        RequestDelegate next,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _next = next;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ICurrentUser currentUser,
        IdentitySessionManager sessionManager)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/connect/", System.StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Account/", System.StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        if (currentUser.IsAuthenticated)
        {
            var sessionId = currentUser.FindClaim(AbpClaimTypes.SessionId)?.Value;
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                // 非事务即可：撤销判定只读；Touch 是单行 UPDATE，autoSave 随 CompleteAsync 落库
                using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
                var session = await sessionManager.FindBySessionIdAsync(sessionId);
                if (session == null)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        error = "session_revoked",
                        error_description = "The identity session has been revoked."
                    });
                    return;
                }

                // 续期 LastAccessed，否则 CleanupWorker 会把仍在发 API 的会话当僵尸删掉。
                // Touch 写库失败会随本 UoW 抛出，不吞——会话状态不能撒谎。
                await sessionManager.TouchIfStaleAsync(session);
                await uow.CompleteAsync();
            }
        }

        await _next(context);
    }
}
