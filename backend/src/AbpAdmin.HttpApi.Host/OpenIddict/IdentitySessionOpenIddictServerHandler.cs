using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Identity;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// T4.1：在 OpenIddict ProcessSignIn 写入 <see cref="IdentitySession"/>，并把 session_id 放进 principal。
/// 与 <see cref="TenantActivationOpenIddictServerHandler"/> 同一事件、略晚执行：租户已解析、授权码尚未核销。
/// </summary>
public class IdentitySessionOpenIddictServerHandler : IOpenIddictServerHandler<ProcessSignInContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessSignInContext>()
            .UseScopedHandler<IdentitySessionOpenIddictServerHandler>()
            .SetOrder(TenantActivationOpenIddictServerHandler.Descriptor.Order + 100)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    private readonly IdentitySessionManager _sessionManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public IdentitySessionOpenIddictServerHandler(
        IdentitySessionManager sessionManager,
        IHttpContextAccessor httpContextAccessor,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _sessionManager = sessionManager;
        _httpContextAccessor = httpContextAccessor;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public virtual async ValueTask HandleAsync(ProcessSignInContext context)
    {
        if (context.Principal?.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var userIdValue = context.Principal.FindFirstValue(AbpClaimTypes.UserId)
                          ?? context.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userIdValue.IsNullOrWhiteSpace() || !Guid.TryParse(userIdValue, out var userId))
        {
            return;
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);

        var http = _httpContextAccessor.HttpContext;
        var ip = http?.Connection.RemoteIpAddress?.ToString();
        var userAgent = http?.Request.Headers.UserAgent.ToString();
        if (userAgent?.Length > 256)
        {
            userAgent = userAgent[..256];
        }

        var existingSessionId = context.Principal.FindFirstValue(AbpClaimTypes.SessionId);

        // 设备类型：客户端在 token/authorize 请求上带可选 device 参数（Mobile 端接约定传
        // device=Mobile），缺省/未知值回退 Web——这是防并发登录「同类型设备互踢」的区分轴
        var device = IdentitySessionDeviceResolver.Resolve(
            context.Transaction.Request?.GetParameter(IdentitySessionDeviceResolver.DeviceParameterName)?.ToString());

        IdentitySession? session;
        if (existingSessionId.IsNullOrWhiteSpace())
        {
            // 全新登录：建立新会话并执行防并发登录策略
            session = await _sessionManager.CreateAsync(
                userId,
                Guid.NewGuid().ToString("N"),
                device,
                userAgent,
                context.ClientId,
                ip);
        }
        else
        {
            // 刷新类授权（refresh token 等）重放的是旧 sessionId：走续期语义——
            // 行还在则刷新，行已没了（被吊销/互踢）绝不能重建。否则持有 refresh token
            // 的客户端被踢后靠静默续期原地复活，吊销与防并发登录全部失效（六透镜审查 H1）。
            // 旧 sessionId 保留在重发的票里，后续 API 请求被会话校验中间件 401（session_revoked）。
            session = await _sessionManager.RenewAsync(
                userId,
                existingSessionId,
                ip);
            if (session == null)
            {
                await uow.CompleteAsync();
                return;
            }
        }

        var identity = context.Principal.Identity as ClaimsIdentity;
        if (identity != null && !identity.HasClaim(AbpClaimTypes.SessionId, session.SessionId))
        {
            // destinations 必须显式声明：OpenIddict PrepareAccessTokenPrincipal 会剔除
            // 无 access-token destination 的 claim（六透镜/E2E 轮 High1——缺失时
            // password-grant 票不含 session_id，会话吊销/互踢对该类票全部失效）
            identity.AddClaim(new Claim(AbpClaimTypes.SessionId, session.SessionId)
                .SetDestinations(OpenIddictConstants.Destinations.AccessToken,
                                 OpenIddictConstants.Destinations.IdentityToken));
        }

        await uow.CompleteAsync();
    }
}
