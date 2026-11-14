using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Permissions;
using AbpAdmin.Collection.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp;
using Volo.Abp.Authorization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.MultiTenancy;
using Volo.Abp.PermissionManagement;
using Volo.Abp.Security.Claims;

namespace AbpAdmin.Authorization;

public class CollectionOperationsEndpointTests
{
    [RequiresHostDatabaseFact]
    public async Task Balance_Adjustments_Expose_Protected_Approval_Actions()
    {
        var app = await HostTestBed.HostTask;
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        var approve = endpoints.Single(endpoint =>
            endpoint.RoutePattern.RawText?.TrimStart('/') == "api/app/collection-balance-adjustment/{id}/approve");
        approve.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.ShouldBe(new[] { "POST" });
        approve.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
        var policies = approve.Metadata.GetOrderedMetadata<IAuthorizeData>().Select(item => item.Policy).ToList();
        policies.ShouldContain(CollectionPermissions.BalanceAdjustments.View);
        policies.ShouldContain(CollectionPermissions.BalanceAdjustments.Approve);
    }

    [RequiresHostDatabaseFact]
    public async Task Network_Configuration_Accepts_A_Body_And_Requires_Authorization()
    {
        var app = await HostTestBed.HostTask;
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();
        endpoints.Any(endpoint =>
            endpoint.RoutePattern.RawText?.TrimStart('/') == "api/app/collection-network-admin/api-key" &&
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.Contains("POST")).ShouldBeFalse();
        endpoints.Any(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/') ==
            "api/app/collection-network-admin/set-rpc-url").ShouldBeFalse();
        foreach (var path in new[] { "configure-network", "configure-token", "add-api-key" })
        {
            var endpoint = endpoints.Single(item => item.RoutePattern.RawText?.TrimStart('/') ==
                "api/app/collection-network-admin/" + path);
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.ShouldBe(new[] { "POST" });
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(item => item.Policy == CollectionPermissions.Network.Admin).ShouldBeTrue();
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!;
            action.Parameters.ShouldContain(parameter => parameter.BindingInfo != null &&
                parameter.BindingInfo.BindingSource == BindingSource.Body);
        }
    }

    [RequiresHostDatabaseFact]
    public async Task Only_Operations_Reads_Relax_The_Network_Admin_Policy()
    {
        var app = await HostTestBed.HostTask;
        var readActions = new[]
        {
            nameof(CollectionNetworkAdminAppService.GetDepositOrdersAsync),
            nameof(CollectionNetworkAdminAppService.GetSweepTasksAsync),
            nameof(CollectionNetworkAdminAppService.GetVaultChecksAsync),
            nameof(CollectionNetworkAdminAppService.GetAnkrBalanceAsync)
        };
        var actions = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() ==
                typeof(CollectionNetworkAdminAppService))
            .ToList();

        actions.ShouldNotBeEmpty();
        foreach (var endpoint in actions)
        {
            var method = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.MethodInfo.Name;
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
            policies.ShouldNotBeEmpty();
            policies.Any(item => item.Policy == CollectionPermissions.Network.Admin)
                .ShouldBe(!readActions.Contains(method), method);
            if (readActions.Contains(method))
            {
                endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.ShouldBe(new[] { "GET" });
            }
        }

        actions.Select(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!.MethodInfo.Name)
            .Where(readActions.Contains).Distinct().Count().ShouldBe(readActions.Length);
    }

    [RequiresHostDatabaseFact]
    public async Task Operations_Reads_Require_One_Read_Permission_And_Host_Context()
    {
        var app = await HostTestBed.HostTask;
        using var scope = app.Services.CreateScope();
        var services = scope.ServiceProvider;
        var accessor = services.GetRequiredService<ICurrentPrincipalAccessor>();
        var service = services.GetRequiredService<CollectionNetworkAdminAppService>();
        var permissionSeeder = services.GetRequiredService<IPermissionDataSeeder>();

        ClaimsPrincipal CreatePrincipal(Guid id) => new(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, id.ToString()),
        }, "Testing"));

        async Task ReadOperationsAsync()
        {
            (await service.GetDepositOrdersAsync(0, 10)).ShouldNotBeNull();
            (await service.GetSweepTasksAsync(0, 10)).ShouldNotBeNull();
            (await service.GetVaultChecksAsync(0, 10)).ShouldNotBeNull();
            (await service.GetAnkrBalanceAsync()).ShouldNotBeNull();
        }

        using (accessor.Change(CreatePrincipal(Guid.NewGuid())))
        {
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetDepositOrdersAsync(0, 10));
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetSweepTasksAsync(0, 10));
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetVaultChecksAsync(0, 10));
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetAnkrBalanceAsync());
        }

        var observer = Guid.NewGuid();
        await permissionSeeder.SeedAsync(UserPermissionValueProvider.ProviderName, observer.ToString(),
            new[] { CollectionPermissions.Collections.Default });
        using (accessor.Change(CreatePrincipal(observer)))
        {
            await ReadOperationsAsync();
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetStatusAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetApiKeysAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.SetChainSwitchesAsync());
        }

        var networkAdmin = Guid.NewGuid();
        await permissionSeeder.SeedAsync(UserPermissionValueProvider.ProviderName, networkAdmin.ToString(),
            new[] { CollectionPermissions.Network.Admin });
        using (accessor.Change(CreatePrincipal(networkAdmin)))
        {
            await ReadOperationsAsync();
            (await service.GetNetworksAsync()).ShouldNotBeNull();
            await service.SetChainSwitchesAsync(); // 不传开关值，不写设置。
        }

        using (services.GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
        using (accessor.Change(CreatePrincipal(observer)))
        {
            var denied = await Should.ThrowAsync<BusinessException>(() => service.GetDepositOrdersAsync(0, 10));
            denied.Code.ShouldBe(CollectionErrorCodes.HostOnly);
            var balanceDenied = await Should.ThrowAsync<BusinessException>(() => service.GetAnkrBalanceAsync());
            balanceDenied.Code.ShouldBe(CollectionErrorCodes.HostOnly);
        }
    }
}
