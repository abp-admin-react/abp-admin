using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace AbpAdmin.ClientErrors;

[AllowAnonymous]
[ApiController]
public sealed class ClientErrorFaultController : ControllerBase
{
    public const string BoomPath = "/api/client-error-test/boom";
    public const string FailureResultPath = "/api/client-error-test/failure-result";
    public const string Marker = "INTERNAL_ONLY_MARKER";

    [HttpGet(BoomPath)]
    public IActionResult Boom()
    {
        throw new InvalidOperationException($"{Marker} SELECT * FROM secrets");
    }

    [HttpGet(FailureResultPath)]
    public IActionResult FailureResult()
    {
        var mapper = HttpContext.RequestServices.GetRequiredService<ClientFailureMapper>();
        return Ok(mapper.CreateFailureFromException(
            new InvalidOperationException($"{Marker} SELECT * FROM secrets"),
            correlationId: "fixture-correlation"));
    }
}
