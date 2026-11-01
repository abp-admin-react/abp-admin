using System;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Authorization.Permissions;

namespace AbpAdmin.Desensitization;

/// <summary>
/// 脱敏字段的「明文权限」判定门（对标 ruoyi 脱敏注解的 SpEL disable 属性）。
/// 序列化发生在无 DI 上下文的 converter 里，因此以静态 Current 暴给 converter，
/// 由 Host 模块启动时回填；未初始化/无 HTTP 上下文/权限服务异常一律判无权限（fail-closed，保持脱敏）。
/// 判定结果按请求 + 权限名缓存在 HttpContext.Items，避免列表页逐行重复查询权限。
/// </summary>
public class MaskingPermissionGate : ISingletonDependency
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MaskingPermissionGate> _logger;

    public static MaskingPermissionGate? Current { get; set; }

    public MaskingPermissionGate(IHttpContextAccessor httpContextAccessor, ILogger<MaskingPermissionGate> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public bool IsPlaintextAllowed(string? permission)
    {
        if (string.IsNullOrEmpty(permission))
        {
            return false;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext?.RequestServices == null)
        {
            return false;
        }

        var cacheKey = "__masking_plaintext:" + permission;
        if (httpContext.Items.TryGetValue(cacheKey, out var cached) && cached is bool cachedResult)
        {
            return cachedResult;
        }

        var result = CheckPermission(httpContext, permission, cacheKey);
        httpContext.Items[cacheKey] = result;
        return result;
    }

    private bool CheckPermission(HttpContext httpContext, string permission, string cacheKey)
    {
        try
        {
            var permissionChecker = httpContext.RequestServices.GetService<IPermissionChecker>();
            if (permissionChecker == null)
            {
                return false;
            }

            // ASP.NET Core 无同步上下文，converter 同步路径上等待不会死锁
            return permissionChecker.IsGrantedAsync(permission).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // fail-closed（脱敏），但持久故障不能零诊断静默降级：同请求只记一次
            var errorKey = cacheKey + ":error";
            if (!httpContext.Items.ContainsKey(errorKey))
            {
                httpContext.Items[errorKey] = true;
                _logger.LogWarning(ex, "脱敏明文权限判定失败，本请求按无权限处理（保持脱敏）：{Permission}", permission);
            }

            return false;
        }
    }
}
