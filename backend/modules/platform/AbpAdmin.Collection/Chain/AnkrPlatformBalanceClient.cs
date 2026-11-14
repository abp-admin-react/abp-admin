using System;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using HttpAgent;
using Microsoft.AspNetCore.WebUtilities;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Collection.Chain;

/// <summary>Reads account billing data from Ankr's Platform API.</summary>
public sealed class AnkrPlatformBalanceClient(IHttpRemoteService http) : ITransientDependency
{
    private const string BalanceEndpoint = "https://mainnet.multirpc.ankr.com/api/v1/auth/balance";

    internal async Task<(string Credits, string BalanceUsd)?> ReadAsync(
        string platformAuthKey, string? group, CancellationToken cancellationToken)
        => (await TestAsync(platformAuthKey, group, cancellationToken)).Balance;

    /// <summary>Returns the authentication outcome and balances from the same provider request.</summary>
    internal async Task<(string Status, (string Credits, string BalanceUsd)? Balance)> TestAsync(
        string platformAuthKey, string? group, CancellationToken cancellationToken)
    {
        var (status, body) = await FetchAsync(platformAuthKey, group, cancellationToken);
        if (ProviderCredentialStatus.IsAuthRejection(status))
        {
            return (ProviderCredentialStatus.Invalid, null);
        }
        var balance = ReadBalance(body);
        return (balance == null ? ProviderCredentialStatus.Unavailable : ProviderCredentialStatus.Available, balance);
    }

    private async Task<(HttpStatusCode? Status, JsonElement? Body)> FetchAsync(
        string platformAuthKey, string? group, CancellationToken cancellationToken)
    {
        var endpoint = group == null ? BalanceEndpoint : QueryHelpers.AddQueryString(BalanceEndpoint, "group", group);
        var request = HttpRequestBuilder.Get(endpoint).WithHeader("Authorization", $"Bearer {platformAuthKey}");
        var (status, body) = await ChainProviderProbe.ReadJsonAsync(http, request, cancellationToken);
        return (status, status == HttpStatusCode.OK ? body : null);
    }

    private static (string Credits, string BalanceUsd)? ReadBalance(JsonElement? body)
    {
        if (body is not { } json)
        {
            return null;
        }

        var credits = ReadNumber(json, "balance");
        var balanceUsd = ReadNumber(json, "balance_usd");
        return credits == null || balanceUsd == null ? null : (credits, balanceUsd);
    }

    private static string? ReadNumber(JsonElement json, string property)
    {
        if (!json.TryGetProperty(property, out var value))
        {
            return null;
        }

        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };

        return decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount)
            ? amount.ToString(CultureInfo.InvariantCulture)
            : null;
    }
}
