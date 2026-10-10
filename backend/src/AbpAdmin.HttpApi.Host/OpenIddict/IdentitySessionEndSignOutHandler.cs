using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AbpAdmin.Identity;
using Microsoft.AspNetCore.Http;
using OpenIddict.Server;
using Volo.Abp.Security.Claims;
using Volo.Abp.Uow;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// 安全审计 H-2：end-session（/connect/endsession）时吊销 <see cref="IdentitySession"/>。
/// 此前登出只销毁认证 Cookie，会话行原样留在库里——被盗的 refresh token 在用户"已登出"
/// 之后仍可通过 <see cref="IdentitySessionManager.RenewAsync"/> 续期换新令牌（会话行在
/// = 续期合法）。配合 IdentitySessionOpenIddictServerHandler 对"会话已删"的拒绝，
/// 登出后旧 refresh token 立即死亡，构成完整的登出吊销链。
/// 2026-10-10 浏览器实测：SPA 登出（oidc signoutRedirect 携 id_token_hint）后，
/// 登出前保存的 refresh token 兑换 → 400 invalid_grant，会话表对应行已删。
///
/// 会话定位：end-session 请求的 id_token_hint（oidc-client-ts signoutRedirect 必带）——
/// session_id claim 在签发时写入了 id_token destination。这里只解 payload 不验签：
/// 实测（2026-10-10 极端轮）OpenIddict 对无效 hint 采取宽松语义（垃圾 JWT 也能走完
/// end-session 流程，302 而非报错），签名校验不能作为本处理器的前提；安全性由另一侧
/// 保证——"凭 hint 吊销"在最坏情况下（伪造 hint）需要先知道不可猜测的 session_id 值
///（Guid N 形式 122 bit 随机），等于只能吊销自己已持有令牌的会话，与合法登出同边界。
/// 无 id_token_hint（或读不到 session_id）时静默跳过——行为退化为登出前状态，不劣化。
/// 注：不能走 Cookie principal——认证 Cookie 建立于登录页（早于令牌签发），不含
/// session_id claim；OpenIddict 7.x 也没有跨事件读取已验 hint principal 的公开 API。
/// </summary>
public class IdentitySessionEndSignOutHandler : IOpenIddictServerHandler<ProcessSignOutContext>
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessSignOutContext>()
            .UseScopedHandler<IdentitySessionEndSignOutHandler>()
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    private readonly IdentitySessionManager _sessionManager;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public IdentitySessionEndSignOutHandler(
        IdentitySessionManager sessionManager,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _sessionManager = sessionManager;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public virtual async ValueTask HandleAsync(ProcessSignOutContext context)
    {
        var idTokenHint = context.Transaction.Request?.IdTokenHint;
        if (string.IsNullOrWhiteSpace(idTokenHint))
        {
            return;
        }

        var sessionId = TryReadSessionIdFromJwtPayload(idTokenHint);
        if (sessionId.IsNullOrWhiteSpace())
        {
            return;
        }

        var session = await _sessionManager.FindBySessionIdAsync(sessionId!);
        if (session == null)
        {
            return;
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        await _sessionManager.RevokeAsync(session.Id);
        await uow.CompleteAsync();
    }

    /// <summary>
    /// 只读 JWT payload 里的 session_id claim（<see cref="AbpClaimTypes.SessionId"/> 序列化后的
    /// 键名），不验签（理由见类注释）。解析失败一律返回 null——本处理器是尽力而为的吊销，
    /// 任何读不出都按"无信息"跳过。
    /// </summary>
    private static string? TryReadSessionIdFromJwtPayload(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2: payload += "=="; break;
                case 3: payload += "="; break;
            }

            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, AbpClaimTypes.SessionId, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }

            return null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
