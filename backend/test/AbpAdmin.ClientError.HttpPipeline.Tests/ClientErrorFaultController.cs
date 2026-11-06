using System;
using System.Threading.Tasks;
using AbpAdmin.HttpApi.ClientErrors;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp;

namespace AbpAdmin.ClientErrors;

[AllowAnonymous]
[ApiController]
[ClientErrorAudience(ClientErrorAudience.Public)]
public sealed class ClientErrorFaultController : ControllerBase
{
    public const string BoomPath = "/api/client-error-test/boom";
    public const string FailureResultPath = "/api/client-error-test/failure-result";
    public const string NotFoundPath = "/api/client-error-test/not-found";
    public const string ForbiddenPath = "/api/client-error-test/forbidden";
    public const string StartedPath = "/api/client-error-test/started";
    public const string ConnectBoomPath = "/connect/client-error-test/boom";
    public const string GatewayBoomPath = "/v1/client-error-test/boom";
    public const string StringFailurePath = "/api/client-error-test/string-failure";
    public const string RegisteredPath = "/api/client-error-test/registered";
    public const string Marker = "INTERNAL_ONLY_MARKER";

    [HttpGet(BoomPath)]
    public IActionResult Boom()
    {
        throw new InvalidOperationException($"{Marker} SELECT * FROM secrets");
    }

    /// <summary>
    /// A01：已登记内部码必须投影为公开码和当前语言文案，异常 Data 不得原样下发。
    /// </summary>
    [HttpGet(RegisteredPath)]
    public IActionResult Registered()
    {
        throw new BusinessException(ClientErrorCodes.NotFound)
            .WithData(Marker, "SELECT * FROM secrets");
    }

    [HttpGet(FailureResultPath)]
    public IActionResult FailureResult()
    {
        var mapper = HttpContext.RequestServices.GetRequiredService<ClientFailureMapper>();
        return Ok(mapper.CreateFailureFromException(
            new InvalidOperationException($"{Marker} SELECT * FROM secrets"),
            correlationId: "fixture-correlation"));
    }

    [HttpGet(StringFailurePath)]
    public IActionResult StringFailure()
    {
        var mapper = HttpContext.RequestServices.GetRequiredService<ClientFailureMapper>();
        return Ok(mapper.CreateFailureFromMessage(
            $"{Marker} SELECT * FROM secrets",
            correlationId: null));
    }

    [HttpGet(NotFoundPath)]
    public Task<IActionResult> NotFoundHelper()
    {
        return ClientErrorActionResults.NotFoundAsync(HttpContext);
    }

    [HttpGet(ForbiddenPath)]
    public Task<IActionResult> ForbiddenHelper()
    {
        return ClientErrorActionResults.ForbiddenAsync(HttpContext);
    }

    [HttpGet(StartedPath)]
    public async Task Started()
    {
        Response.StatusCode = 200;
        Response.ContentType = "text/plain; charset=utf-8";
        await Response.StartAsync();
        await Response.WriteAsync("partial-body");
        await Response.Body.FlushAsync();
        throw new InvalidOperationException($"{Marker} SELECT * FROM secrets");
    }

    [HttpGet(ConnectBoomPath)]
    public IActionResult ConnectBoom()
    {
        throw new InvalidOperationException($"{Marker} SELECT * FROM secrets");
    }

    [HttpGet(GatewayBoomPath)]
    public IActionResult GatewayBoom()
    {
        throw new InvalidOperationException($"{Marker} SELECT * FROM secrets");
    }
}
