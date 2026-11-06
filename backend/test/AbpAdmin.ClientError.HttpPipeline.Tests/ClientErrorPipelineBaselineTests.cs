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
        body.ShouldContain("correlationId");
    }

    [Fact]
    public async Task Registered_Business_Exception_Should_Return_Public_Code_And_Culture()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        using var request = new HttpRequestMessage(HttpMethod.Get, ClientErrorFaultController.RegisteredPath);
        request.Headers.AcceptLanguage.ParseAdd("en");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain(ClientErrorCodes.NotFound);
        body.ShouldContain("The requested resource was not found.");
        body.ShouldNotContain(ClientErrorFaultController.Marker);
        body.ShouldNotContain("SELECT * FROM secrets");
        body.ShouldNotContain("请求的资源不存在");
    }

    [Fact]
    public async Task Uncaught_Api_Exception_Should_Use_Request_Culture()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        using var request = new HttpRequestMessage(HttpMethod.Get, ClientErrorFaultController.BoomPath);
        request.Headers.AcceptLanguage.ParseAdd("en");
        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldContain("An unexpected error occurred");
        body.ShouldNotContain("系统繁忙");
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
    public async Task NotFound_Helper_Should_Return_Safe_Envelope()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        using var response = await client.GetAsync(ClientErrorFaultController.NotFoundPath);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        body.ShouldContain(ClientErrorCodes.NotFound);
        body.ShouldNotContain(ClientErrorFaultController.Marker);
        body.Contains("<html", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }

    [Fact]
    public async Task Forbidden_Helper_Should_Return_Safe_Envelope()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        using var response = await client.GetAsync(ClientErrorFaultController.ForbiddenPath);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        body.ShouldContain(ClientErrorCodes.Forbidden);
        body.ShouldNotContain(ClientErrorFaultController.Marker);
    }

    [Fact]
    public async Task String_Failure_Result_Should_Use_OperationFailed()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        using var response = await client.GetAsync(ClientErrorFaultController.StringFailurePath);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        body.ShouldContain(ClientErrorCodes.OperationFailed);
        body.ShouldNotContain(ClientErrorFaultController.Marker);
        body.ShouldNotContain("SELECT * FROM secrets");
    }

    [Fact]
    public async Task Connect_Uncaught_Exception_Should_Return_OAuth_Without_Html()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        using var response = await client.GetAsync(ClientErrorFaultController.ConnectBoomPath);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldContain("server_error");
        body.ShouldNotContain(ClientErrorFaultController.Marker);
        body.ShouldNotContain("stackTrace");
        body.Contains("<html", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
        body.Contains("\"error\":", StringComparison.OrdinalIgnoreCase).ShouldBeTrue();
        body.ShouldNotContain(ClientErrorCodes.Unexpected);
    }

    [Fact]
    public async Task Gateway_Uncaught_Exception_Should_Return_Fixed_Internal_Shape()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        using var response = await client.GetAsync(ClientErrorFaultController.GatewayBoomPath);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        body.ShouldContain("INTERNAL");
        body.ShouldNotContain(ClientErrorFaultController.Marker);
        body.Contains("<html", StringComparison.OrdinalIgnoreCase).ShouldBeFalse();
    }

    [Fact]
    public async Task Started_Response_Should_Not_Append_Json_Envelope()
    {
        var (_, client) = await ClientErrorPipelineHost.HostTask;

        try
        {
            using var response = await client.GetAsync(ClientErrorFaultController.StartedPath);
            var body = await response.Content.ReadAsStringAsync();
            var mixed = body.Contains("partial-body", StringComparison.Ordinal)
                        && body.Contains("\"error\"", StringComparison.Ordinal);
            mixed.ShouldBeFalse("已写出的响应不得再追加 JSON 信封。");
            if (body.Contains("partial-body", StringComparison.Ordinal))
            {
                body.ShouldNotContain(ClientErrorCodes.Unexpected);
            }
            else
            {
                ClientErrorTestLogBuffer.Instance.Contains(ClientErrorFaultController.Marker).ShouldBeTrue();
            }
        }
        catch (HttpRequestException)
        {
            ClientErrorTestLogBuffer.Instance.Contains(ClientErrorFaultController.Marker).ShouldBeTrue();
        }
    }

    [Fact]
    public async Task Converter_Should_Keep_Internal_Details_When_Http_Response_Is_Safe()
    {
        var (app, client) = await ClientErrorPipelineHost.HostTask;
        var exception = new InvalidOperationException($"{ClientErrorFaultController.Marker} SELECT * FROM secrets");
        var converter = app.Services.GetRequiredService<Volo.Abp.AspNetCore.ExceptionHandling.IExceptionToErrorInfoConverter>();
        var info = converter.Convert(exception, options =>
        {
            options.SendExceptionsDetailsToClients = true;
            options.SendStackTraceToClients = true;
        });

        info.Message.ShouldNotBeNull();
        info.Message.ShouldContain(ClientErrorFaultController.Marker);

        using var response = await client.GetAsync(ClientErrorFaultController.BoomPath);
        var body = await response.Content.ReadAsStringAsync();
        body.ShouldNotContain(ClientErrorFaultController.Marker);
        body.ShouldNotContain("stackTrace");
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
