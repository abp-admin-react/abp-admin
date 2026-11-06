using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using AbpAdmin.ClientErrors;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Localization;

namespace AbpAdmin.HttpApi.ClientErrors.Localization;

/// <summary>
/// Shared 出口的公开资源视图。管理员调用也不放宽；管理专用入口用 <see cref="AllowAdminResources"/> 绕过。
/// </summary>
public sealed class PublicLocalizationViewFilter : ISingletonDependency
{
    internal static readonly AsyncLocal<bool> AllowAdminResources = new();

    private static readonly HashSet<string> PublicResourceNames = new(StringComparer.Ordinal)
    {
        "AbpUi",
        "AbpAccount",
        "AbpTenantManagement",
        "AbpValidation",
        "AbpIdentity",
        "AbpAspNetCore",
        "LeptonXLite",
        "LeptonXLiteTheme",
        "EasyAbp.Abp.SettingUi",
    };

    private readonly IOptions<AbpLocalizationOptions> _localizationOptions;

    public PublicLocalizationViewFilter(IOptions<AbpLocalizationOptions> localizationOptions)
    {
        _localizationOptions = localizationOptions;
    }

    /// <summary>管理专用本地化入口临时放开管理员资源。using 结束即恢复。</summary>
    public static IDisposable EnableAdminScope()
    {
        var previous = AllowAdminResources.Value;
        AllowAdminResources.Value = true;
        return new DisposeAction(() => AllowAdminResources.Value = previous);
    }

    /// <summary>裁剪 application-configuration 的本地化字典。管理员调用也不放宽。</summary>
    public ApplicationConfigurationDto FilterConfiguration(ApplicationConfigurationDto dto)
    {
        if (AllowAdminResources.Value || dto.Localization == null)
        {
            return dto;
        }

        if (dto.Localization.Values is { Count: > 0 })
        {
            dto.Localization.Values = FilterDictionary(dto.Localization.Values);
        }

        if (dto.Localization.Resources is { Count: > 0 })
        {
            dto.Localization.Resources = FilterResources(dto.Localization.Resources);
        }

        return dto;
    }

    /// <summary>裁剪 application-localization。与配置接口同一公开资源白名单。</summary>
    public ApplicationLocalizationDto FilterLocalization(ApplicationLocalizationDto dto)
    {
        if (AllowAdminResources.Value || dto.Resources == null)
        {
            return dto;
        }

        dto.Resources = FilterResources(dto.Resources);
        return dto;
    }

    /// <summary>
    /// 公开资源：固定框架名，或实现 IClientStaticResource 的客户资源。
    /// 其余管理资源不出现在 Shared 出口。
    /// </summary>
    public bool IsPublicResource(string resourceName)
    {
        if (PublicResourceNames.Contains(resourceName))
        {
            return true;
        }

        return _localizationOptions.Value.Resources.TryGetValue(resourceName, out var resource)
               && resource is LocalizationResource localizationResource
               && typeof(IClientStaticResource).IsAssignableFrom(localizationResource.ResourceType);
    }

    private Dictionary<string, Dictionary<string, string>> FilterDictionary(
        IDictionary<string, Dictionary<string, string>> values)
    {
        var filtered = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (IsPublicResource(pair.Key))
            {
                filtered[pair.Key] = pair.Value;
            }
        }

        return filtered;
    }

    private Dictionary<string, ApplicationLocalizationResourceDto> FilterResources(
        IDictionary<string, ApplicationLocalizationResourceDto> resources)
    {
        var filtered = new Dictionary<string, ApplicationLocalizationResourceDto>(StringComparer.Ordinal);
        foreach (var pair in resources)
        {
            if (IsPublicResource(pair.Key))
            {
                filtered[pair.Key] = pair.Value;
            }
        }

        return filtered;
    }

    private sealed class DisposeAction : IDisposable
    {
        private readonly Action _action;

        public DisposeAction(Action action) => _action = action;

        public void Dispose() => _action();
    }
}
