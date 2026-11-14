using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace AbpAdmin.Authorization;

public class CollectionPaymentAdminEndpointTests
{
    [RequiresHostDatabaseFact]
    public async Task Host_Exposes_Only_Protected_Read_Endpoints_For_Payment_Records()
    {
        var app = await HostTestBed.HostTask;
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .ToList();

        const string route = "api/app/collection-payment-admin";
        var records = endpoints.Where(endpoint =>
            (endpoint.RoutePattern.RawText?.TrimStart('/') ?? "")
                .StartsWith(route, StringComparison.Ordinal)).ToList();
        records.Select(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/'))
            .OrderBy(template => template)
            .ShouldBe(new[] { route, route + "/{id}" });

        foreach (var endpoint in records)
        {
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.ShouldBe(new[] { "GET" });
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>()
                .Any(data => data.Policy == CollectionPermissions.Payments.View)
                .ShouldBeTrue();
        }

        endpoints.Any(endpoint => (endpoint.RoutePattern.RawText?.TrimStart('/') ?? "")
            .StartsWith("api/payment-service/", StringComparison.Ordinal)).ShouldBeFalse();
    }

    [RequiresHostDatabaseFact]
    public async Task Operations_Use_Host_Permissions_And_Callback_Has_Only_A_Post_Route()
    {
        var app = await HostTestBed.HostTask;
        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var callback = Find("api/collection/channel-notifications/{channel}/{merchantAccountId}");
        callback.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.ShouldBe(new[] { "POST" });
        callback.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();
        callback.Metadata.GetMetadata<SkipStatusCodePagesAttribute>().ShouldNotBeNull();

        var query = Find("api/app/collection-payments/{id}/query");
        AssertPolicy(query, CollectionPermissions.Payments.Reconcile);
        AssertPolicy(Find("api/app/collection-refunds"), CollectionPermissions.Payments.Refund);
        AssertPolicy(Find("api/app/collection-refunds/{id}/query"), CollectionPermissions.Payments.Refund);
        AssertPolicy(Find("api/app/collection-payments/{id}/refunds"), CollectionPermissions.Payments.View);

        RouteEndpoint Find(string route) =>
            endpoints.Single(endpoint => endpoint.RoutePattern.RawText?.TrimStart('/') == route);

        static void AssertPolicy(RouteEndpoint endpoint, string policy)
        {
            endpoint.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => data.Policy == policy).ShouldBeTrue();
        }
    }
}
