using System;
using Volo.Abp;

namespace AbpAdmin.Biz.Template.Configuration;

/// <summary>
/// <see cref="BizTemplateOptions.EnableClientErrorSample"/> 的启动门禁：
/// 仅 Development / Testing 允许打开，其它环境名（含 Production）拒绝启用。
/// </summary>
public static class BizTemplateClientErrorSampleGuard
{
    public const string TestingEnvironmentName = "Testing";

    public const string DevelopmentEnvironmentName = "Development";

    /// <summary>Development 与 Testing（Host 测试夹具）允许打开样例入口。</summary>
    public static bool IsAllowedEnvironment(string? environmentName)
    {
        return string.Equals(environmentName, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase)
               || string.Equals(environmentName, TestingEnvironmentName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>开关打开且环境不在白名单时抛 AbpInitializationException，阻止宿主启动。</summary>
    public static void EnsureCanEnable(bool enabled, IAbpHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        EnsureCanEnable(enabled, environment.EnvironmentName);
    }

    /// <summary>与日志/异常文案同一口径：禁止在 Production 等环境把样例当客户入口。</summary>
    public static void EnsureCanEnable(bool enabled, string? environmentName)
    {
        if (!enabled)
        {
            return;
        }

        if (IsAllowedEnvironment(environmentName))
        {
            return;
        }

        throw new AbpInitializationException(
            "BizTemplate.EnableClientErrorSample 禁止在环境 " +
            $"'{environmentName ?? "(null)"}' 启用：该开关只用于错误协议接入样例，" +
            "仅 Development/Testing 允许打开，不能作为生产客户入口或多租户授权模板。");
    }
}
