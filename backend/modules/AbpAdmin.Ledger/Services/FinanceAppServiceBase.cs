using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Localization;
using AbpAdmin.Ledger.Services.Dtos;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization;

namespace AbpAdmin.Ledger.Services;

/// <summary>
/// 财务应用服务基类：带业务错误码的权限检查 + 列表参数归一。
/// RequireAnyAsync：未登录 → 401；已登录无权且传了 errorCode → 该码映射 403（如 AdjustCreateDenied）；
/// 属主校验由子类用 Scope.Agent / ApplicantId 做，未命中时抛 403 业务码或 404（勿泄露跨属主存在性时用 404）。
/// </summary>
public abstract class FinanceAppServiceBase : ApplicationService
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    protected FinanceAppServiceBase()
    {
        LocalizationResource = typeof(LedgerResource);
    }

    /// <summary>
    /// 任一权限满足即可。未登录 → AbpAuthorizationException（401）；
    /// 已登录但无权：errorCode 非空抛带码 BusinessException（映射 403），否则抛 ABP 通用授权异常（403）。
    /// </summary>
    protected async Task RequireAnyAsync(string? errorCode, params string[] permissions)
    {
        if (!CurrentUser.IsAuthenticated)
        {
            throw new AbpAuthorizationException(code: AbpAuthorizationErrorCodes.GivenPolicyHasNotGranted);
        }

        foreach (var p in permissions)
        {
            if (await AuthorizationService.IsGrantedAsync(p))
            {
                return;
            }
        }

        if (errorCode == null)
        {
            throw new AbpAuthorizationException(code: AbpAuthorizationErrorCodes.GivenPolicyHasNotGranted)
                .WithData("PolicyName", string.Join(" | ", permissions));
        }

        throw new BusinessException(errorCode).WithData("Permission", string.Join(" | ", permissions));
    }

    protected Guid RequireUserId() =>
        CurrentUser.Id ?? throw new AbpAuthorizationException(code: AbpAuthorizationErrorCodes.GivenPolicyHasNotGranted);

    protected static (int Skip, int Take) Paging(FinListInput input) =>
        (Math.Max(0, input.SkipCount),
         input.MaxResultCount <= 0 ? DefaultPageSize : Math.Min(MaxPageSize, input.MaxResultCount));

    protected static List<TEnum>? ParseStatuses<TEnum>(string? raw) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var result = new List<TEnum>();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse<TEnum>(part, ignoreCase: true, out var v) || !Enum.IsDefined(v))
            {
                throw new BusinessException(FinanceErrorCodes.InvalidEvent).WithData("Field", "status").WithData("Value", part);
            }

            result.Add(v);
        }

        return result;
    }
}
