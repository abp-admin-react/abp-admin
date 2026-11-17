using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using HttpAgent;

namespace AbpAdmin.Collection.Chain;

/// <summary>Keeps endpoint credentials out of HttpAgent diagnostics, including its exception logger.</summary>
internal sealed class PrivateChainEndpointHandler : DelegatingHandler
{
    private static readonly HttpRequestOptionsKey<Uri> EndpointKey = new("Collection.PrivateEndpoint");

    internal static HttpRequestBuilder Get(string endpoint) => Build(endpoint, origin => HttpRequestBuilder.Get(origin));

    internal static HttpRequestBuilder Post(string endpoint) => Build(endpoint, origin => HttpRequestBuilder.Post(origin));

    private static HttpRequestBuilder Build(string value, Func<string, HttpRequestBuilder> create)
    {
        var endpoint = new Uri(value, UriKind.Absolute);
        var origin = new UriBuilder(endpoint.Scheme, endpoint.IdnHost, endpoint.Port).Uri;
        return create(origin.AbsoluteUri)
            .SetOnPreSendRequest(request => request.Options.Set(EndpointKey, endpoint));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!request.Options.TryGetValue(EndpointKey, out var endpoint))
        {
            return await base.SendAsync(request, cancellationToken);
        }

        // HttpAgent logs the message URI after SendAsync faults. Keep it and the exception safe at that boundary.
        var origin = request.RequestUri;
        request.RequestUri = endpoint;
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new HttpRequestException(ex.HttpRequestError, "The chain provider request failed.",
                ex.InnerException is TimeoutException ? new TimeoutException() : null, ex.StatusCode);
        }
        finally
        {
            request.RequestUri = origin;
        }
    }
}
