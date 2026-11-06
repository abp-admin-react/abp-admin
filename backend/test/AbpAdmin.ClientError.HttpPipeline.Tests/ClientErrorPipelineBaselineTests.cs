using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using AbpAdmin.HttpApi.ClientErrors;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Volo.Abp.AspNetCore.ExceptionHandling;
using Volo.Abp.AspNetCore.Mvc.ExceptionHandling;
using Volo.Abp.AspNetCore.Tracing;
using Xunit;

namespace AbpAdmin.ClientErrors;

public class ClientErrorPipelineBaselineTests
{
    [Fact]
    public async Task Replaced_Services_Should_Resolve_To_Client_Error_Implementations()
    {
        var (app, _) = await ClientErrorPipelineHost.HostTask;
        var services = app.Services;

        services.GetRequiredService<AbpExceptionFilter>().ShouldBeOfType<ClientErrorExceptionFilter>();
        services.GetRequiredService<AbpExceptionHandlingMiddleware>()
            .ShouldBeOfType<ClientErrorAbpExceptionHandlingMiddleware>();
        services.GetRequiredService<AbpCorrelationIdMiddleware>()
            .ShouldBeOfType<ClientErrorCorrelationIdMiddleware>();
        services.GetServices<AbpExceptionFilter>().Count().ShouldBe(1);
        services.GetServices<AbpExceptionHandlingMiddleware>().Count().ShouldBe(1);
        services.GetServices<AbpCorrelationIdMiddleware>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task Uncaught_Api_Exception_Should_Return_Safe_Envelope()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        using var response = await client.GetAsync(ClientErrorFaultController.BoomPath);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldContain(ClientErrorCodes.Unexpected);
        body.ShouldNotContain(ClientErrorFaultController.Marker);
        body.ShouldNotContain("SELECT * FROM secrets");
        body.ShouldNotContain("stackTrace");
        body.Contains("<html", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        var detailsIndex = body.IndexOf("\"details\"", StringComparison.OrdinalIgnoreCase);
        detailsIndex.ShouldBeGreaterThanOrEqualTo(0);
        body.Substring(detailsIndex, Math.Min(40, body.Length - detailsIndex))
            .ShouldContain("null");
    }

    [Fact]
    public async Task Incoming_Correlation_Header_Should_Not_Become_Canonical()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;
        const string incoming = "forged-parent-id.1";

        using var first = new HttpRequestMessage(HttpMethod.Get, "/api/abp/application-configuration");
        first.Headers.TryAddWithoutValidation("X-Correlation-Id", incoming);
        using var firstResponse = await client.SendAsync(first);

        using var second = new HttpRequestMessage(HttpMethod.Get, "/api/abp/application-configuration");
        second.Headers.TryAddWithoutValidation("X-Correlation-Id", incoming);
        using var secondResponse = await client.SendAsync(second);

        firstResponse.Headers.TryGetValues("X-Correlation-Id", out var firstIds).ShouldBeTrue();
        secondResponse.Headers.TryGetValues("X-Correlation-Id", out var secondIds).ShouldBeTrue();

        var firstId = firstIds!.Single();
        var secondId = secondIds!.Single();
        firstId.ShouldNotBe(incoming);
        secondId.ShouldNotBe(incoming);
        firstId.ShouldNotBe(secondId);
        firstId.Length.ShouldBe(32);
        secondId.Length.ShouldBe(32);
    }

    [Fact]
    public async Task Http_200_Failure_Result_Should_Not_Contain_Internal_Marker()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        using var response = await client.GetAsync(ClientErrorFaultController.FailureResultPath);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain(ClientErrorCodes.Unexpected);
        body.ShouldNotContain(ClientErrorFaultController.Marker);
        body.ShouldNotContain("SELECT * FROM secrets");
    }

    [Fact]
    public async Task Uncaught_Exception_Should_Remain_In_Server_Logs()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        using var response = await client.GetAsync(ClientErrorFaultController.BoomPath);
        var body = await response.Content.ReadAsStringAsync();

        body.ShouldNotContain(ClientErrorFaultController.Marker);
        ClientErrorTestLogBuffer.Instance.Contains(ClientErrorFaultController.Marker).ShouldBeTrue();
    }

    [Fact]
    public async Task Unauthenticated_Api_Should_Return_Public_Envelope()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        using var response = await client.GetAsync("/api/identity/users");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        body.ShouldContain(ClientErrorCodes.AuthenticationRequired);
        body.Contains("<html", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        body.ShouldNotContain("stackTrace");
    }
}
