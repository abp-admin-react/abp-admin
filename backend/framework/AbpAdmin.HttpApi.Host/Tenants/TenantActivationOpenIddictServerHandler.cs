using System;
using System.Threading.Tasks;
using AbpAdmin.Localization;
using AbpAdmin.Tenants;
using Microsoft.Extensions.Localization;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.TenantManagement;
using Volo.Abp.Uow;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace AbpAdmin.OpenIddict;

/// <summary>
/// T2.8 SaaS Pro 缺口：登录时校验租户激活状态。
/// Passive 拒绝（提示租户已停用）；ActiveWithLimitedTime 且 ActivationEndDate 已过期拒绝（提示租户已到期）。
///
/// 接入点验证结论（对本仓库 10.6 依赖反编译核实，不要相信直觉）：
/// 1. 登录页在开源包 Volo.Abp.Account.Web(.OpenIddict) 里（OpenIddictSupportedLoginModel），
///    授权码登录的令牌发放全部汇聚到 OpenIddict 服务器的 ProcessSignIn 事件
///    （授权端点签发 code 与 /connect/token 各 grant 签发令牌都会派发该事件）。
/// 2. 不能选 ValidateAuthorizationRequest / ValidateTokenRequest 阶段：
///    OpenIddict 服务器处理器（OpenIddictServerAspNetCoreHandler）是 IAuthenticationRequestHandler，
///    这两个阶段在 UseAuthentication 中间件内执行，早于 UseMultiTenancy，ICurrentTenant 尚未解析。
///    而 ProcessSignIn 是 MVC 端点（ABP 的 AuthorizeController/TokenController）调用 SignIn 时派发的，
///    此刻 UseMultiTenancy 已执行（本仓库管线：UseAuthentication → UseAbpOpenIddictValidation →
///    UseMultiTenancy → UseUnitOfWork），租户已解析。
/// 3. 已知残余缺口：/connect/token 的授权码交换与刷新请求若不带 __tenant 且不走租户子域，
///    解析不到租户则跳过校验——但授权码发放环节（登录）已拦过一次，满足验收口径。
///
/// 处理器顺序：ValidateSignInDemand（内置第一个 ProcessSignIn 处理器）之后、RedeemTokenEntry 之前，
/// 使被拒的登录不会消耗授权码条目。
/// </summary>
public class TenantActivationOpenIddictServerHandler :
    IOpenIddictServerHandler<ProcessSignInContext>,
    IScopedDependency
{
    public static OpenIddictServerHandlerDescriptor Descriptor { get; } =
        OpenIddictServerHandlerDescriptor.CreateBuilder<ProcessSignInContext>()
            .UseScopedHandler<TenantActivationOpenIddictServerHandler>()
            .SetOrder(OpenIddictServerHandlers.ValidateSignInDemand.Descriptor.Order + 500)
            .SetType(OpenIddictServerHandlerType.Custom)
            .Build();

    private readonly ICurrentTenant _currentTenant;
    private readonly ITenantRepository _tenantRepository;
    private readonly TenantActivationChecker _activationChecker;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IStringLocalizer<AbpAdminResource> _localizer;

    public TenantActivationOpenIddictServerHandler(
        ICurrentTenant currentTenant,
        ITenantRepository tenantRepository,
        TenantActivationChecker activationChecker,
        IUnitOfWorkManager unitOfWorkManager,
        IStringLocalizer<AbpAdminResource> localizer)
    {
        _currentTenant = currentTenant;
        _tenantRepository = tenantRepository;
        _activationChecker = activationChecker;
        _unitOfWorkManager = unitOfWorkManager;
        _localizer = localizer;
    }

    public virtual async ValueTask HandleAsync(ProcessSignInContext context)
    {
        if (!_currentTenant.Id.HasValue)
        {
            return;
        }

        // 独立 UoW：不依赖外层是否有活动 UoW（6.5 节约定）
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var tenant = await _tenantRepository.FindAsync(_currentTenant.Id.Value);
        if (tenant != null)
        {
            try
            {
                _activationChecker.Check(tenant);
            }
            catch (BusinessException ex) when (ex.Code != null)
            {
                context.Reject(
                    OpenIddictConstants.Errors.AccessDenied,
                    _localizer[ex.Code]);
                return;
            }
        }

        await uow.CompleteAsync();
    }
}
