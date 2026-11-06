using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using AbpAdmin.ClientErrors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace AbpAdmin.HttpApi.ClientErrors;

/// <summary>
/// 为每个 MVC 动作写入受众元数据。属性优先；其余按显式名单，不按权限前缀自动升级。
/// </summary>
public sealed class ClientErrorAudienceConvention : IApplicationModelConvention
{
    private static readonly HashSet<string> SharedControllerTypeNames = new(StringComparer.Ordinal)
    {
        "Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations.AbpApplicationConfigurationController",
        "Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations.AbpApplicationLocalizationController",
        "Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations.AbpApplicationConfigurationScriptController",
        "Volo.Abp.AspNetCore.Mvc.Localization.AbpApplicationLocalizationScriptController",
        "Volo.Abp.AspNetCore.Mvc.ProxyScripting.AbpServiceProxyScriptController",
        "Pages.Abp.MultiTenancy.AbpTenantController",
        "Volo.Abp.Account.AccountController",
        "Volo.Abp.Account.Public.Web.Areas.Account.Controllers.AccountController",
    };

    private static readonly HashSet<string> PublicControllerTypeNames = new(StringComparer.Ordinal)
    {
        "AbpAdmin.Controllers.FileShareController",
        "AbpAdmin.Controllers.GdprDownloadController",
        "AbpAdmin.ClientErrors.ClientErrorFaultController",
        "AbpAdmin.Biz.Template.Client.Controllers.BizProjectClientController",
    };

    public void Apply(ApplicationModel application)
    {
        foreach (var controller in application.Controllers)
        {
            var controllerType = controller.ControllerType.AsType();
            var controllerAudience = controllerType.GetCustomAttribute<ClientErrorAudienceAttribute>(inherit: true);
            foreach (var action in controller.Actions)
            {
                var method = action.ActionMethod;
                var actionAudience = method.GetCustomAttribute<ClientErrorAudienceAttribute>(inherit: true);
                var permissions = CollectPermissions(controller, action);
                if (IsSettingUiController(controllerType) && permissions.Count == 0)
                {
                    permissions.Add("EasyAbp.Abp.SettingUi.ShowSettingPage");
                }

                var audience = actionAudience?.Audience
                               ?? controllerAudience?.Audience
                               ?? InferAudience(controllerType, action, permissions);

                var parameterSignature = "(" + string.Join(", ",
                    action.Parameters.Select(p => p.ParameterInfo.ParameterType.Name)) + ")";
                var declaringType = controllerType.FullName ?? controllerType.Name;
                var httpMethod = action.Selectors
                    .SelectMany(s => s.ActionConstraints.OfType<Microsoft.AspNetCore.Mvc.ActionConstraints.HttpMethodActionConstraint>())
                    .SelectMany(c => c.HttpMethods)
                    .FirstOrDefault();
                var routeTemplate = action.Selectors
                    .Select(s => s.AttributeRouteModel?.Template)
                    .FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));

                var metadata = new ClientErrorEndpointMetadata
                {
                    ExitId = $"{declaringType}.{method.Name}{parameterSignature}",
                    DeclaringType = declaringType,
                    MethodName = method.Name,
                    ParameterSignature = parameterSignature,
                    HttpMethod = httpMethod,
                    RouteTemplate = routeTemplate,
                    Audience = audience,
                    Protocol = InferProtocol(controllerType, routeTemplate),
                    RequiredPermissions = permissions,
                };

                action.Filters.Add(metadata);
                foreach (var selector in action.Selectors)
                {
                    selector.EndpointMetadata.Add(metadata);
                }
            }
        }
    }

    private static ClientErrorAudience InferAudience(
        Type controllerType,
        ActionModel action,
        IReadOnlyCollection<string> permissions)
    {
        var fullName = controllerType.FullName ?? controllerType.Name;
        if (SharedControllerTypeNames.Contains(fullName))
        {
            return ClientErrorAudience.Shared;
        }

        if (PublicControllerTypeNames.Contains(fullName)
            || fullName.Contains(".Client.Controllers.", StringComparison.Ordinal))
        {
            return ClientErrorAudience.Public;
        }

        var route = action.Selectors
            .Select(s => s.AttributeRouteModel?.Template)
            .FirstOrDefault() ?? string.Empty;
        if (route.StartsWith("api/client/", StringComparison.OrdinalIgnoreCase)
            || route.Contains("/client/", StringComparison.OrdinalIgnoreCase))
        {
            return ClientErrorAudience.Public;
        }

        if (permissions.Count > 0)
        {
            return ClientErrorAudience.AdminCompatibility;
        }

        return ClientErrorAudience.Public;
    }

    private static ErrorProtocol InferProtocol(Type controllerType, string? routeTemplate)
    {
        var fullName = controllerType.FullName ?? string.Empty;
        if (fullName.Contains("OpenIddict", StringComparison.OrdinalIgnoreCase)
            || (routeTemplate?.StartsWith("connect/", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return ErrorProtocol.OAuth;
        }

        return ErrorProtocol.AbpEnvelope;
    }

    private static bool IsSettingUiController(Type controllerType)
    {
        return string.Equals(controllerType.Name, "SettingUiController", StringComparison.Ordinal)
               && (controllerType.FullName?.Contains("SettingUi", StringComparison.Ordinal) ?? false);
    }

    private static List<string> CollectPermissions(ControllerModel controller, ActionModel action)
    {
        var names = new List<string>();
        foreach (var attribute in controller.Attributes.Concat(action.Attributes).OfType<IAuthorizeData>())
        {
            if (string.IsNullOrWhiteSpace(attribute.Policy))
            {
                continue;
            }

            foreach (var part in attribute.Policy.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!names.Contains(part, StringComparer.Ordinal))
                {
                    names.Add(part);
                }
            }
        }

        return names;
    }
}
