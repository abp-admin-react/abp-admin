using System;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HttpAgent;
using Microsoft.AspNetCore.WebUtilities;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Collection.Chain;

internal static class ProviderCredentialStatus
{
    public const string Available = "available";
    public const string Invalid = "invalid";
    public const string PlanRequired = "planRequired";
    public const string Unavailable = "unavailable";

    public static bool IsAuthRejection(HttpStatusCode? status) =>
        status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

/// <summary>Small, read-only probes of the same BSC APIs the chain workers use.</summary>
public sealed class ChainProviderProbe(IHttpRemoteService http) : ITransientDependency
{
    internal const string HttpClientName = "collection-provider-probe";

    internal async Task<string> TestAnkrRpcAsync(string key, CancellationToken cancellationToken)
    {
        var request = HttpRequestBuilder.Post(AnkrBscChainGateway.AnkrBscRpcUrlBase + key)
            .SetJsonContent(new { jsonrpc = "2.0", method = "eth_chainId", @params = Array.Empty<object>(), id = 1 });
        var (status, body) = await ReadJsonAsync(http, request, cancellationToken);
        if (body is { } json && json.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object
            && error.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var number))
        {
            // A temporary blockchain/client feature restriction may also use 403.
            return number is -32049 or -32050 or -32051 or -32052 or -32053 or -32054 or -32060 or -32079
                ? ProviderCredentialStatus.Invalid : ProviderCredentialStatus.Unavailable;
        }
        if (ProviderCredentialStatus.IsAuthRejection(status))
        {
            return ProviderCredentialStatus.Invalid;
        }

        return status == HttpStatusCode.OK && body is { } success
            && string.Equals(ReadString(success, "result"), "0x38", StringComparison.OrdinalIgnoreCase)
            ? ProviderCredentialStatus.Available : ProviderCredentialStatus.Unavailable;
    }

    internal async Task<string> TestEtherscanAsync(string key, CancellationToken cancellationToken)
    {
        var request = AnkrBscChainGateway.BuildEtherscanUrl(AnkrBscChainGateway.DefaultEtherscanV2Endpoint,
            "&address=0x0000000000000000000000000000000000000000&startblock=1&endblock=1&page=1&offset=1&sort=asc");
        var uri = QueryHelpers.AddQueryString(request, "apikey", key);
        var (status, body) = await ReadJsonAsync(http, HttpRequestBuilder.Get(uri), cancellationToken);
        if (body is { } response && ReadString(response, "result") is { } reason
            && reason.StartsWith("Free API access is not supported for this chain", StringComparison.OrdinalIgnoreCase))
        {
            return ProviderCredentialStatus.PlanRequired;
        }
        if (ProviderCredentialStatus.IsAuthRejection(status))
        {
            return ProviderCredentialStatus.Invalid;
        }
        if (status != HttpStatusCode.OK || body is not { } json)
        {
            return ProviderCredentialStatus.Unavailable;
        }

        var result = ReadString(json, "result");
        var message = ReadString(json, "message");
        if (json.TryGetProperty("result", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            var providerStatus = ReadString(json, "status");
            if ((providerStatus == "1" && message == "OK")
                || (providerStatus == "0" && message == "No transactions found" && items.GetArrayLength() == 0))
            {
                return ProviderCredentialStatus.Available;
            }
        }

        return IsEtherscanAuthError(result) || IsEtherscanAuthError(message)
            ? ProviderCredentialStatus.Invalid : ProviderCredentialStatus.Unavailable;
    }

    private static bool IsEtherscanAuthError(string? message) => message != null
        && (message.Equals("Invalid API Key", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("Missing/Invalid API Key", StringComparison.OrdinalIgnoreCase)
            || message.StartsWith("OK-Missing/Invalid API Key", StringComparison.OrdinalIgnoreCase));

    private static string? ReadString(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static async Task<(HttpStatusCode? Status, JsonElement? Body)> ReadJsonAsync(
        IHttpRemoteService http, HttpRequestBuilder request, CancellationToken cancellationToken)
    {
        request.SetHttpClientName(HttpClientName)
            .DisableProfiler()
            .WithHeader("Accept", "application/json");
        HttpStatusCode? status = null;
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            status = response?.StatusCode;
            if (response == null)
            {
                return (status, null);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var body = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return (status, body.RootElement.ValueKind == JsonValueKind.Object ? body.RootElement.Clone() : null);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested
            && ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            return (status, null);
        }
    }
}
