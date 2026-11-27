using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Permissions;
using AbpAdmin.Collection.Services;
using AbpAdmin.Grids;
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
        const string route = "api/app/collection-network-admin/";
        var expectedReads = new Dictionary<string, (string Verb, string Path)>
        {
            [nameof(CollectionNetworkAdminAppService.GetDepositOrdersAsync)] = ("GET", "deposit-orders"),
            [nameof(CollectionNetworkAdminAppService.QueryDepositOrdersAsync)] = ("POST", "deposit-orders/query"),
            [nameof(CollectionNetworkAdminAppService.GetSweepTasksAsync)] = ("GET", "sweep-tasks"),
            [nameof(CollectionNetworkAdminAppService.QuerySweepTasksAsync)] = ("POST", "sweep-tasks/query"),
            [nameof(CollectionNetworkAdminAppService.GetVaultChecksAsync)] = ("GET", "vault-checks"),
            [nameof(CollectionNetworkAdminAppService.QueryVaultChecksAsync)] = ("POST", "vault-checks/query"),
            [nameof(CollectionNetworkAdminAppService.GetAnkrBalanceAsync)] = ("GET", "ankr-balance")
        };
        var actions = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() ==
                typeof(CollectionNetworkAdminAppService))
            .ToList();

        actions.ShouldNotBeEmpty();
        foreach (var endpoint in actions)
        {
            var action = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!;
            var method = action.MethodInfo.Name;
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
            policies.ShouldNotBeEmpty();
            var requiresNetworkAdmin = policies.Any(item => item.Policy == CollectionPermissions.Network.Admin);
            if (!expectedReads.Remove(method, out var read))
            {
                requiresNetworkAdmin.ShouldBeTrue(method);
                continue;
            }

            requiresNetworkAdmin.ShouldBeFalse(method);
            (endpoint.RoutePattern.RawText?.TrimStart('/')).ShouldBe(route + read.Path);
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.ShouldBe(new[] { read.Verb });
            if (read.Verb == "POST")
            {
                var body = action.Parameters.Single(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body);
                body.ParameterType.ShouldBe(typeof(GridQueryInput));
                // The browser sends scope in the URL alongside the JSON grid body.
                var scope = action.Parameters.Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Query)
                    .Select(parameter => parameter.Name).OrderBy(name => name).ToArray();
                string[] expectedScope = method switch
                {
                    nameof(CollectionNetworkAdminAppService.QueryDepositOrdersAsync) => ["depositId", "status", "sweepTaskId", "symbol"],
                    nameof(CollectionNetworkAdminAppService.QuerySweepTasksAsync) => ["status", "sweepTaskId"],
                    _ => []
                };
                scope.ShouldBe(expectedScope, method);
            }
        }

        expectedReads.Keys.ShouldBeEmpty();
    }

    [RequiresHostDatabaseFact]
    public async Task Scanner_Management_Protects_Credentials_And_Exposes_Explicit_Routes()
    {
        var app = await HostTestBed.HostTask;
        const string route = "api/app/collection-scan-provider";
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() ==
                typeof(CollectionScanProviderAppService)).ToArray();

        endpoints.Length.ShouldBe(10);
        foreach (var endpoint in endpoints)
        {
            var path = endpoint.RoutePattern.RawText!.TrimStart('/');
            path.ShouldStartWith(route);
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
            var policies = endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>();
            policies.ShouldNotBeEmpty();
            policies.Any(item => item.Policy == CollectionPermissions.Network.Admin)
                .ShouldBe(path != route + "/dashboard-balance", path);
        }

        foreach (var action in new[] { nameof(CollectionScanProviderAppService.CreateAsync),
            nameof(CollectionScanProviderAppService.UpdateAsync), nameof(CollectionScanProviderAppService.TestAsync),
            nameof(CollectionScanProviderAppService.TestBalanceAsync) })
        {
            var endpoint = endpoints.Single(item => item.Metadata.GetMetadata<ControllerActionDescriptor>()!.MethodInfo.Name == action);
            var descriptor = endpoint.Metadata.GetMetadata<ControllerActionDescriptor>()!;
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.ShouldNotContain("GET");
            descriptor.Parameters.ShouldContain(parameter => parameter.BindingInfo != null && parameter.BindingInfo.BindingSource == BindingSource.Body);
        }
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

        var grid = new GridQueryInput { MaxResultCount = 10 };
        Func<Task<object>>[] reads =
        [
            async () => await service.GetDepositOrdersAsync(0, 10),
            async () => await service.QueryDepositOrdersAsync(grid),
            async () => await service.GetSweepTasksAsync(0, 10),
            async () => await service.QuerySweepTasksAsync(grid),
            async () => await service.GetVaultChecksAsync(0, 10),
            async () => await service.QueryVaultChecksAsync(grid),
            async () => await service.GetAnkrBalanceAsync()
        ];

        using (accessor.Change(CreatePrincipal(Guid.NewGuid())))
        {
            foreach (var read in reads)
            {
                await Should.ThrowAsync<AbpAuthorizationException>(read);
            }
        }

        var observer = Guid.NewGuid();
        await permissionSeeder.SeedAsync(UserPermissionValueProvider.ProviderName, observer.ToString(),
            new[] { CollectionPermissions.Collections.Default });
        using (accessor.Change(CreatePrincipal(observer)))
        {
            foreach (var read in reads)
            {
                (await read()).ShouldNotBeNull();
            }
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetStatusAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.GetApiKeysAsync());
            await Should.ThrowAsync<AbpAuthorizationException>(() => service.SetChainSwitchesAsync());
        }

        var networkAdmin = Guid.NewGuid();
        await permissionSeeder.SeedAsync(UserPermissionValueProvider.ProviderName, networkAdmin.ToString(),
            new[] { CollectionPermissions.Network.Admin });
        using (accessor.Change(CreatePrincipal(networkAdmin)))
        {
            foreach (var read in reads)
            {
                (await read()).ShouldNotBeNull();
            }
            (await service.GetNetworksAsync()).ShouldNotBeNull();
            await service.SetChainSwitchesAsync(); // 不传开关值，不写设置。
        }

        using (services.GetRequiredService<ICurrentTenant>().Change(Guid.NewGuid()))
        using (accessor.Change(CreatePrincipal(observer)))
        {
            foreach (var read in reads)
            {
                var denied = await Should.ThrowAsync<BusinessException>(read);
                denied.Code.ShouldBe(CollectionErrorCodes.HostOnly);
            }
        }
    }
}
