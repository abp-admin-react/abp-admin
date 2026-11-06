using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Volo.Abp.AspNetCore.Tracing;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Tracing;

namespace AbpAdmin.ClientErrors;

/// <summary>
/// 每个外部 HTTP 请求生成新的 canonical 关联号，不信任传入头。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(AbpCorrelationIdMiddleware))]
public class ClientErrorCorrelationIdMiddleware : AbpCorrelationIdMiddleware
{
    public const string IncomingHintItemKey = "AbpAdmin.ClientErrors.IncomingCorrelationHint";

    private static readonly Regex AllowedIncoming = new(
        @"^[A-Za-z0-9._-]{1,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly AbpCorrelationIdOptions _correlationIdOptions;

    public ClientErrorCorrelationIdMiddleware(
        IOptions<AbpCorrelationIdOptions> options,
        ICorrelationIdProvider correlationIdProvider)
        : base(options, correlationIdProvider)
    {
        _correlationIdOptions = options.Value;
    }

    protected override string? GetCorrelationIdFromRequest(HttpContext context)
    {
        var headerName = _correlationIdOptions.HttpHeaderName;
        var incomingValues = context.Request.Headers[headerName];
        if (incomingValues.Count == 1)
        {
            var incoming = incomingValues.ToString();
            if (AllowedIncoming.IsMatch(incoming))
            {
                context.Items[IncomingHintItemKey] = incoming;
            }
        }

        var canonical = Guid.NewGuid().ToString("N");
        context.Request.Headers[headerName] = canonical;
        return canonical;
    }

    protected override void CheckAndSetCorrelationIdOnResponse(
        HttpContext httpContext,
        AbpCorrelationIdOptions options,
        string? correlationId)
    {
        var captured = correlationId;
        httpContext.Response.OnStarting(() =>
        {
            if (options.SetResponseHeader && !string.IsNullOrWhiteSpace(captured))
            {
                httpContext.Response.Headers[options.HttpHeaderName] = captured;
            }

            return Task.CompletedTask;
        });
    }
}
