using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace AbpAdmin.Monitoring;

/// <summary>
/// 登录后顶栏读的运行信息：程序集版本、操作系统当地时间和时区。
/// 不含机器名、路径或配置。任意已登录用户可读。
/// </summary>
[Authorize]
public class RuntimeInfoAppService : AbpAdminAppService, IRuntimeInfoAppService
{
    public virtual Task<RuntimeInfoDto> GetAsync()
    {
        return Task.FromResult(RuntimeInfoFactory.Create(
            ResolveProductAssembly(),
            DateTimeOffset.UtcNow,
            TimeZoneInfo.Local));
    }

    /// <summary>
    /// 优先用正在运行的 AbpAdmin 宿主程序集（发布出去的就是它）。
    /// 测试宿主的入口程序集不是产品，回落到本程序集，版本仍来自 common.props。
    /// </summary>
    private static Assembly ResolveProductAssembly()
    {
        var entry = Assembly.GetEntryAssembly();
        var name = entry?.GetName().Name;
        if (entry != null && name != null && name.StartsWith("AbpAdmin", StringComparison.Ordinal))
        {
            return entry;
        }

        return typeof(RuntimeInfoAppService).Assembly;
    }
}
