using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Entities;
using HttpAgent;
using Microsoft.AspNetCore.WebUtilities;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Collection.Chain;

/// <summary>Read-only BSC deposit APIs. No transaction signing or broadcast methods are exposed here.</summary>
public sealed class BscDepositProviderClient(IHttpRemoteService http) : ITransientDependency
{
    internal const string HttpClientName = "collection-deposit-scan";
    private const long MainnetChainId = CollectionConsts.BscMainnetChainId;
    private const string Usdt = "0x55d398326f99059ff775485246999027b3197955";
    private const int PageSize = 1000;
    private const int MaxPages = 10;
    private const int ReceiptBatchLimit = 16;

    internal async Task<string> ProbeAsync(ChainApiKeyProvider provider, string key, CancellationToken ct)
    {
        try
        {
            var client = Create(provider, key, cap: int.MaxValue);
            var head = await client.GetHeadAsync(ct);
            var block = Math.Max(1, head - Settings.ChainDefaults.Confirmations);
            await client.ReadLogsAsync(Usdt, block, block, new HashSet<string>(), ct);
            return ProviderCredentialStatus.Available;
        }
        catch (DepositScanReadException ex)
        {
            return ex.Code;
        }
    }

    internal IBscDepositScanSource Create(ChainApiKeyProvider provider, string credential, int cap) => provider switch
    {
        ChainApiKeyProvider.RpcBsc => new RpcSource(this, AnkrBscChainGateway.AnkrBscRpcUrlBase + credential, cap),
        ChainApiKeyProvider.EtherscanV2 => new ExplorerSource(this, credential),
        _ => throw new DepositScanReadException(DepositScanFailure.InvalidConfiguration),
    };

    internal IBscDepositScanSource CreateCustomRpc(string url, int cap) => new RpcSource(this, url, cap);

    private async Task<JsonElement> RequestAsync(HttpRequestBuilder request, CancellationToken ct)
    {
        try
        {
            request.SetHttpClientName(HttpClientName).DisableProfiler().WithHeader("Accept", "application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseContentRead, ct);
            if (response is not { IsSuccessStatusCode: true })
            {
                throw new DepositScanReadException(HttpFailure(response?.StatusCode));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new DepositScanReadException(DepositScanFailure.Unavailable);
            }
            return document.RootElement.Clone();
        }
        catch (HttpRequestException ex)
        {
            var code = ex.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded
                ? DepositScanFailure.RangeIncomplete
                : !ex.StatusCode.HasValue && ex.InnerException is TimeoutException
                    ? DepositScanFailure.Timeout : HttpFailure(ex.StatusCode);
            throw new DepositScanReadException(code);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new DepositScanReadException(DepositScanFailure.Timeout);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            throw new DepositScanReadException(DepositScanFailure.Unavailable);
        }
    }

    private static string HttpFailure(HttpStatusCode? status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ProviderCredentialStatus.Invalid,
        HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests => DepositScanFailure.Quota,
        HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => DepositScanFailure.Timeout,
        HttpStatusCode.RequestEntityTooLarge => DepositScanFailure.RangeIncomplete,
        _ => DepositScanFailure.Unavailable,
    };

    private static string MessageFailure(string? reason)
    {
        if (Contains(reason, "not supported for this chain") || Contains(reason, "paid plan"))
        {
            return ProviderCredentialStatus.PlanRequired;
        }
        if (Contains(reason, "limit reached") || Contains(reason, "limit exhausted")
            || Contains(reason, "too many invalid api key attempts"))
        {
            return DepositScanFailure.Quota;
        }
        if (Contains(reason, "invalid api key") || Contains(reason, "missing api key")
            || Contains(reason, "unauthorized") || Contains(reason, "forbidden"))
        {
            return ProviderCredentialStatus.Invalid;
        }
        if (AnkrBscChainGateway.IsQuotaOrAuthMessage(reason) || Contains(reason, "payment required"))
        {
            return DepositScanFailure.Quota;
        }
        if (Contains(reason, "block range") || Contains(reason, "response size")
            || Contains(reason, "query returned more than") || Contains(reason, "result window")
            || Contains(reason, "smaller result dataset"))
        {
            return DepositScanFailure.RangeIncomplete;
        }
        return Contains(reason, "timeout") || Contains(reason, "timed out")
            ? DepositScanFailure.Timeout : DepositScanFailure.Unavailable;
    }

    private static bool Contains(string? value, string text) => value?.Contains(text, StringComparison.OrdinalIgnoreCase) == true;

    private async Task<JsonElement> RpcAsync(string endpoint, string method, object[] parameters, CancellationToken ct)
    {
        var request = PrivateChainEndpointHandler.Post(endpoint)
            .SetJsonContent(new { jsonrpc = "2.0", method, @params = parameters, id = 1 });
        return RpcResult(await RequestAsync(request, ct));
    }

    private static JsonElement RpcResult(JsonElement response)
    {
        if (response.TryGetProperty("error", out var error))
        {
            var number = error.ValueKind == JsonValueKind.Object && error.TryGetProperty("code", out var value)
                && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var code) ? code : 0;
            throw new DepositScanReadException(number switch
            {
                -32049 or -32050 or -32051 or -32052 or -32053 or -32054 or -32060 or -32079 or -32080 =>
                    ProviderCredentialStatus.Invalid,
                -32067 or -32068 or -32069 or -32070 or -32077 or -32090 => DepositScanFailure.Quota,
                -32062 => DepositScanFailure.RangeIncomplete,
                -32071 => DepositScanFailure.Timeout,
                _ => MessageFailure(ReadString(error, "message")),
            });
        }
        if (ReadString(response, "status") == "0")
        {
            throw new DepositScanReadException(MessageFailure(ReadString(response, "result") ?? ReadString(response, "message")));
        }
        return response.TryGetProperty("result", out var result)
            ? result : throw new DepositScanReadException(DepositScanFailure.Unavailable);
    }

    private Task<JsonElement> ExplorerAsync(string key, string module, string action,
        Dictionary<string, string?> query, CancellationToken ct)
    {
        query["chainid"] = MainnetChainId.ToString(CultureInfo.InvariantCulture);
        query["module"] = module;
        query["action"] = action;
        query["apikey"] = key;
        return RequestAsync(PrivateChainEndpointHandler.Get(QueryHelpers.AddQueryString(
            AnkrBscChainGateway.DefaultEtherscanV2Endpoint, query)), ct);
    }

    private static string? ReadString(JsonElement json, string property) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty(property, out var value)
            && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long ReadHead(JsonElement result) =>
        result.ValueKind == JsonValueKind.String && TryQuantity(result.GetString(), out var number) && number > 0
            ? number : throw new DepositScanReadException(DepositScanFailure.Unavailable);

    private static bool TryQuantity(string? value, out long number)
    {
        number = 0;
        return value != null && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(value.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out number)
            && number >= 0;
    }

    private static bool IsHex(string? value, int length) => value != null && value.Length == length + 2
        && value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && value.Skip(2).All(Uri.IsHexDigit);

    private static bool EqualsHex(string? x, string? y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

    private static DepositLog ParseLog(JsonElement log, string contract, long from, long to, bool explorer)
    {
        var txHash = ReadString(log, "transactionHash");
        var blockHash = ReadString(log, "blockHash");
        var data = ReadString(log, "data");
        var index = ReadString(log, "logIndex");
        if (!EqualsHex(ReadString(log, "address"), contract) || !IsHex(txHash, 64) || !IsHex(blockHash, 64)
            || !IsHex(data, 64) || !TryQuantity(ReadString(log, "blockNumber"), out var block) || block < from || block > to
            || !(TryQuantity(index, out var logIndex) || explorer && index == "0x") || logIndex > int.MaxValue
            || (log.TryGetProperty("removed", out var removed) && removed.ValueKind != JsonValueKind.False)
            || !log.TryGetProperty("topics", out var topics) || topics.ValueKind != JsonValueKind.Array
            || topics.GetArrayLength() != 3 || topics.EnumerateArray().Any(t => t.ValueKind != JsonValueKind.String)
            || !EqualsHex(topics[0].GetString(), AnkrBscChainGateway.TransferTopic)
            || !IsAddressTopic(topics[1].GetString()) || !IsAddressTopic(topics[2].GetString()))
        {
            throw new DepositScanReadException(DepositScanFailure.EvidenceInvalid);
        }

        var transfer = new TokenTransferLog(txHash!.ToLowerInvariant(), (int)logIndex,
            AnkrBscChainGateway.ToAddress(topics[1].GetString()!).ToLowerInvariant(),
            AnkrBscChainGateway.ToAddress(topics[2].GetString()!).ToLowerInvariant(),
            AnkrBscChainGateway.HexToDecimalString(data!), block);
        return new DepositLog(transfer, blockHash!.ToLowerInvariant());
    }

    private sealed record DepositLog(TokenTransferLog Transfer, string BlockHash);

    private static bool IsAddressTopic(string? topic) => IsHex(topic, 64) && topic!.AsSpan(2, 24).IndexOfAnyExcept('0') < 0;

    private sealed class RpcSource(BscDepositProviderClient owner, string endpoint, int cap) : IBscDepositScanSource
    {
        public bool RequiresReconciliation => false;

        public async Task<long> GetHeadAsync(CancellationToken ct)
        {
            var chain = await owner.RpcAsync(endpoint, "eth_chainId", [], ct);
            if (chain.ValueKind != JsonValueKind.String || !TryQuantity(chain.GetString(), out var chainId) || chainId != MainnetChainId)
            {
                throw new DepositScanReadException(DepositScanFailure.InvalidConfiguration);
            }
            return ReadHead(await owner.RpcAsync(endpoint, "eth_blockNumber", [], ct));
        }

        public async Task<IReadOnlyList<TokenTransferLog>> ReadLogsAsync(string contract, long from, long to,
            IReadOnlySet<string> watched, CancellationToken ct)
        {
            try
            {
                var logs = await QueryAsync(contract, from, to, [AnkrBscChainGateway.TransferTopic], ct);
                return logs.Where(log => watched.Contains(log.To)).ToArray();
            }
            catch (DepositScanReadException ex) when (ex.Code == DepositScanFailure.RangeIncomplete && from == to && watched.Count > 0)
            {
                // A busy single block can still be read completely by querying batches of watched recipients.
                var matches = new List<TokenTransferLog>();
                foreach (var addresses in watched.Chunk(64))
                {
                    await QueryWatchedBatchAsync(contract, from, addresses, matches, ct);
                }
                return matches;
            }
        }

        private async Task QueryWatchedBatchAsync(string contract, long block, string[] addresses,
            List<TokenTransferLog> matches, CancellationToken ct)
        {
            var recipients = addresses.Select(address => "0x" + address[2..].ToLowerInvariant().PadLeft(64, '0')).ToArray();
            try
            {
                var logs = await QueryAsync(contract, block, block, [AnkrBscChainGateway.TransferTopic, null, recipients], ct);
                if (logs.Any(log => !addresses.Contains(log.To, StringComparer.OrdinalIgnoreCase)))
                {
                    throw new DepositScanReadException(DepositScanFailure.EvidenceInvalid);
                }
                matches.AddRange(logs);
            }
            catch (DepositScanReadException ex) when (ex.Code == DepositScanFailure.RangeIncomplete && addresses.Length > 1)
            {
                var middle = addresses.Length / 2;
                await QueryWatchedBatchAsync(contract, block, addresses[..middle], matches, ct);
                await QueryWatchedBatchAsync(contract, block, addresses[middle..], matches, ct);
            }
        }

        private async Task<TokenTransferLog[]> QueryAsync(string contract, long from, long to, object?[] topics,
            CancellationToken ct)
        {
            var parameters = new Dictionary<string, object>
            {
                ["fromBlock"] = AnkrBscChainGateway.ToHex(from),
                ["toBlock"] = AnkrBscChainGateway.ToHex(to),
                ["address"] = contract,
                ["topics"] = topics,
            };
            var result = await owner.RpcAsync(endpoint, "eth_getLogs", [parameters], ct);
            if (result.ValueKind != JsonValueKind.Array)
            {
                throw new DepositScanReadException(DepositScanFailure.Unavailable);
            }
            if (result.GetArrayLength() >= AnkrBscChainGateway.TruncateRatio * cap)
            {
                throw new DepositScanReadException(DepositScanFailure.RangeIncomplete);
            }
            return result.EnumerateArray().Select(log => ParseLog(log, contract, from, to, explorer: false).Transfer).ToArray();
        }
    }

    private sealed class ExplorerSource(BscDepositProviderClient owner, string key) : IBscDepositScanSource
    {
        public bool RequiresReconciliation => true;

        public async Task<long> GetHeadAsync(CancellationToken ct) =>
            ReadHead(RpcResult(await owner.ExplorerAsync(key, "proxy", "eth_blockNumber", [], ct)));

        public async Task<IReadOnlyList<TokenTransferLog>> ReadLogsAsync(string contract, long from, long to,
            IReadOnlySet<string> watched, CancellationToken ct)
        {
            var seen = new HashSet<(string TxHash, int LogIndex)>();
            var matches = new List<DepositLog>();
            var receiptHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var query = new Dictionary<string, string?>
            {
                ["fromBlock"] = from.ToString(CultureInfo.InvariantCulture),
                ["toBlock"] = to.ToString(CultureInfo.InvariantCulture),
                ["address"] = contract,
                ["topic0"] = AnkrBscChainGateway.TransferTopic,
                ["offset"] = PageSize.ToString(CultureInfo.InvariantCulture),
            };
            for (var page = 1; page <= MaxPages; page++)
            {
                query["page"] = page.ToString(CultureInfo.InvariantCulture);
                var response = await owner.ExplorerAsync(key, "logs", "getLogs", query, ct);
                var status = ReadString(response, "status");
                var message = ReadString(response, "message");
                var hasArray = response.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Array;
                if (hasArray && status == "0" && result.GetArrayLength() == 0
                    && string.Equals(message, "No records found", StringComparison.OrdinalIgnoreCase))
                {
                    return await ConfirmAsync(matches, contract, ct);
                }
                if (!hasArray || status != "1" || !string.Equals(message, "OK", StringComparison.OrdinalIgnoreCase))
                {
                    throw new DepositScanReadException(MessageFailure(ReadString(response, "result") ?? message));
                }

                foreach (var entry in result.EnumerateArray())
                {
                    var log = ParseLog(entry, contract, from, to, explorer: true);
                    if (!seen.Add((log.Transfer.TxHash, log.Transfer.LogIndex)))
                    {
                        throw new DepositScanReadException(DepositScanFailure.RangeIncomplete);
                    }
                    if (watched.Contains(log.Transfer.To))
                    {
                        if (from != to && receiptHashes.Add(log.Transfer.TxHash) && receiptHashes.Count > ReceiptBatchLimit)
                        {
                            // Split before sending a burst of verification calls. A single block remains indivisible.
                            throw new DepositScanReadException(DepositScanFailure.RangeIncomplete);
                        }
                        matches.Add(log);
                    }
                }

                if (result.GetArrayLength() < PageSize)
                {
                    return await ConfirmAsync(matches, contract, ct);
                }
            }

            throw new DepositScanReadException(DepositScanFailure.RangeIncomplete);
        }

        private async Task<IReadOnlyList<TokenTransferLog>> ConfirmAsync(List<DepositLog> matches, string contract,
            CancellationToken ct)
        {
            foreach (var transaction in matches.GroupBy(log => log.Transfer.TxHash))
            {
                var response = await owner.ExplorerAsync(key, "proxy", "eth_getTransactionReceipt",
                    new Dictionary<string, string?> { ["txhash"] = transaction.Key }, ct);
                var receipt = RpcResult(response);
                var first = transaction.First();
                if (!EqualsHex(ReadString(receipt, "transactionHash"), transaction.Key)
                    || !EqualsHex(ReadString(receipt, "blockHash"), first.BlockHash)
                    || ReadString(receipt, "status") != "0x1"
                    || !TryQuantity(ReadString(receipt, "blockNumber"), out var block) || block != first.Transfer.BlockNumber
                    || !receipt.TryGetProperty("logs", out var entries) || entries.ValueKind != JsonValueKind.Array)
                {
                    throw new DepositScanReadException(DepositScanFailure.EvidenceInvalid);
                }

                var confirmed = new HashSet<DepositLog>();
                foreach (var entry in entries.EnumerateArray())
                {
                    if (EqualsHex(ReadString(entry, "address"), contract) && entry.TryGetProperty("topics", out var topics)
                        && topics.ValueKind == JsonValueKind.Array && topics.GetArrayLength() > 0
                        && topics[0].ValueKind == JsonValueKind.String
                        && EqualsHex(topics[0].GetString(), AnkrBscChainGateway.TransferTopic))
                    {
                        confirmed.Add(ParseLog(entry, contract, block, block, explorer: false));
                    }
                }

                if (transaction.Any(log => !confirmed.Contains(log)))
                {
                    throw new DepositScanReadException(DepositScanFailure.EvidenceInvalid);
                }
            }

            return matches.Select(log => log.Transfer).ToArray();
        }
    }
}

internal interface IBscDepositScanSource
{
    bool RequiresReconciliation { get; }
    Task<long> GetHeadAsync(CancellationToken ct);
    Task<IReadOnlyList<TokenTransferLog>> ReadLogsAsync(string contract, long from, long to,
        IReadOnlySet<string> watched, CancellationToken ct);
}

internal static class DepositScanFailure
{
    public const string Quota = "quota";
    public const string Timeout = "timeout";
    public const string Unavailable = "unavailable";
    public const string RangeIncomplete = "rangeIncomplete";
    public const string EvidenceInvalid = "evidenceInvalid";
    public const string InvalidConfiguration = "invalidConfiguration";
    public const string NotConfigured = "notConfigured";
    public const string Stale = "stale";
}

internal sealed class DepositScanReadException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
