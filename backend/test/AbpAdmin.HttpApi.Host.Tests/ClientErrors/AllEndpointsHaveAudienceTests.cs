using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.HttpApi.ClientErrors;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class AllEndpointsHaveAudienceTests
{
    private static Task<WebApplication> HostTask => HostTestBed.HostTask;

    [RequiresHostDatabaseFact]
    public async Task Controlled_Mvc_Actions_Should_Have_Audience_Metadata()
    {
        var app = await HostTask;
        var provider = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var missing = new List<string>();
        var invalid = new List<string>();
        var adminWithoutPermission = new List<string>();

        foreach (var action in provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>())
        {
            if (!IsControlled(action.AttributeRouteInfo?.Template, action.ControllerTypeInfo.FullName))
            {
                continue;
            }

            var metadata = action.FilterDescriptors
                .Select(d => d.Filter)
                .OfType<ClientErrorEndpointMetadata>()
                .FirstOrDefault();
            var identity = $"{action.ControllerTypeInfo.FullName}.{action.ActionName}";
            if (metadata == null)
            {
                missing.Add(identity);
                continue;
            }

            if (!Enum.IsDefined(typeof(ClientErrorAudience), metadata.Audience))
            {
                invalid.Add($"{identity}:{metadata.Audience}");
            }

            if (metadata.Audience == ClientErrorAudience.AdminCompatibility
                && metadata.RequiredPermissions.Count == 0)
            {
                adminWithoutPermission.Add(identity);
            }
        }

        missing.ShouldBeEmpty("受控 MVC 动作缺少 ClientErrorEndpointMetadata：\n" + string.Join("\n", missing));
        invalid.ShouldBeEmpty("非法 ClientErrorAudience：\n" + string.Join("\n", invalid));
        adminWithoutPermission.ShouldBeEmpty(
            "AdminCompatibility 未绑定资格权限：\n" + string.Join("\n", adminWithoutPermission));
    }

    [RequiresHostDatabaseFact]
    public async Task Endpoint_Data_Source_Should_Agree_With_Action_Descriptors()
    {
        var app = await HostTask;
        var dataSource = app.Services.GetRequiredService<EndpointDataSource>();
        var missing = new List<string>();

        foreach (var endpoint in dataSource.Endpoints.OfType<RouteEndpoint>())
        {
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (action == null)
            {
                continue;
            }

            if (!IsControlled(endpoint.RoutePattern.RawText, action.ControllerTypeInfo.FullName))
            {
                continue;
            }

            var metadata = endpoint.Metadata.GetMetadata<ClientErrorEndpointMetadata>();
            if (metadata == null)
            {
                missing.Add($"{endpoint.RoutePattern.RawText} {endpoint.DisplayName}");
            }
        }

        missing.ShouldBeEmpty("EndpointDataSource 与动作表不一致，缺受众元数据：\n" + string.Join("\n", missing));
    }

    [RequiresHostDatabaseFact]
    public async Task Shared_Configuration_Controllers_Should_Stay_Shared()
    {
        var app = await HostTask;
        var provider = app.Services.GetRequiredService<IActionDescriptorCollectionProvider>();
        var shared = provider.ActionDescriptors.Items.OfType<ControllerActionDescriptor>()
            .Where(a => a.ControllerTypeInfo.FullName ==
                        "Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations.AbpApplicationConfigurationController")
            .Select(a => a.FilterDescriptors.Select(d => d.Filter).OfType<ClientErrorEndpointMetadata>().FirstOrDefault())
            .ToList();

        shared.ShouldNotBeEmpty();
        shared.ShouldAllBe(m => m != null && m.Audience == ClientErrorAudience.Shared);
    }

    private static bool IsControlled(string? route, string? controllerName)
    {
        var template = (route ?? string.Empty).TrimStart('/');
        if (template.StartsWith("api", StringComparison.OrdinalIgnoreCase)
            || template.StartsWith("connect", StringComparison.OrdinalIgnoreCase)
            || template.StartsWith("v1", StringComparison.OrdinalIgnoreCase)
            || template.StartsWith("signalr-hubs", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return controllerName?.Contains(".Client.Controllers.", StringComparison.Ordinal) == true;
    }
}
