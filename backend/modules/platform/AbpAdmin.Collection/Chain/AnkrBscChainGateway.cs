using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// BSC JSON-RPC 网关，支持按地址使用 Etherscan 补查。节点日志接近上限时，调用方保留游标并缩小区间。
/// 同一服务商仅在配额或认证失败时尝试下一把密钥。签名使用 EIP-155，并在广播前计算交易哈希。
/// </summary>
public class AnkrBscChainGateway : IBscChainGateway
{
    /// <summary>keccak256("Transfer(address,address,uint256)")。</summary>
    public const string TransferTopic = "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef";

    /// <summary>节点返回条数达到配置上限的八成时，按可能截断处理。</summary>
    public const double TruncateRatio = 0.8;

    /// <summary>Ankr 项目密钥使用的 BSC RPC 地址前缀。</summary>
    public const string AnkrBscRpcUrlBase = "https://rpc.ankr.com/bsc/";
    public const string DefaultEtherscanV2Endpoint = "https://api.etherscan.io/v2/api";

    /// <summary>Etherscan V2 端点里 key 参数名（从配置端点截取基座，chainid 等参数跟配置走）。</summary>
    public const string EtherscanApiKeyParam = "apikey=";

    /// <summary>
    /// 组装 Etherscan V2 转账请求 URL。端点可来自密钥池或宿主配置，保留端点已指定的参数并补齐默认项。
    /// 转账查询参数以 "&" 开头。
    /// </summary>
    internal static string BuildEtherscanUrl(string endpoint, string query)
    {
        var queryStart = endpoint.IndexOf('?');
        var parameters = QueryHelpers.ParseQuery(queryStart >= 0 ? endpoint[queryStart..] : null);
        if (!parameters.ContainsKey("chainid"))
        {
            endpoint = QueryHelpers.AddQueryString(endpoint, "chainid", "56");
        }
        if (!parameters.ContainsKey("module"))
        {
            endpoint = QueryHelpers.AddQueryString(endpoint, "module", "account");
        }
        if (!parameters.ContainsKey("action"))
        {
            endpoint = QueryHelpers.AddQueryString(endpoint, "action", "tokentx");
        }
        return endpoint + query;
    }

    private readonly HttpClient _http;
    private readonly ILogger<AnkrBscChainGateway> _logger;
    private readonly string[] _rpcEndpoints;
    private readonly string[] _etherscanEndpoints;
    private readonly int _nodeLogResultCap;
    private readonly int _rpcStart;
    private readonly int _etherscanStart;

    /// <summary>需要至少一个 RPC 端点。Etherscan 端点为空时停用按地址补查。</summary>
    public AnkrBscChainGateway(
        HttpClient http, string[] rpcEndpoints, string[] etherscanEndpoints, int nodeLogResultCap,
        int rpcStartOffset, int etherscanStartOffset, ILogger<AnkrBscChainGateway> logger)
    {
        if (rpcEndpoints.Length == 0)
        {
            throw new ArgumentException("至少配置一个 RPC 端点。");
        }

        _http = http;
        _rpcEndpoints = rpcEndpoints;
        _etherscanEndpoints = etherscanEndpoints;
        _nodeLogResultCap = nodeLogResultCap;
        _rpcStart = ((rpcStartOffset % rpcEndpoints.Length) + rpcEndpoints.Length) % rpcEndpoints.Length;
        _etherscanStart = etherscanEndpoints.Length == 0 ? 0
            : ((etherscanStartOffset % etherscanEndpoints.Length) + etherscanEndpoints.Length) % etherscanEndpoints.Length;
        _logger = logger;
    }

    public async Task<long> GetLatestBlockNumberAsync(CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_blockNumber", Array.Empty<object>(), ct);
        return Convert.ToInt64(result!.Value.GetString()!, 16);
    }

    public async Task<(IReadOnlyList<TokenTransferLog> Logs, bool Complete)> GetTransferLogsAsync(
        string contractAddress, long fromBlock, long toBlock, CancellationToken ct = default)
    {
        var parameters = new Dictionary<string, object>
        {
            ["fromBlock"] = ToHex(fromBlock),
            ["toBlock"] = ToHex(toBlock),
            ["address"] = contractAddress,
            ["topics"] = new object[] { TransferTopic },
        };

        JsonElement result;
        try
        {
            result = (await RpcAsync("eth_getLogs", new object[] { parameters }, ct))!.Value;
        }
        catch (Exception ex) when (ex is ChainRpcException || ex is InvalidOperationException)
        {
            // 节点拒绝查询或缺少 result 时，返回未读完，让调用方缩小区间重试。
            _logger.LogWarning(ex, "getLogs 未读完（{From}..{To}）", fromBlock, toBlock);
            return (Array.Empty<TokenTransferLog>(), false);
        }

        var logs = new List<TokenTransferLog>();
        foreach (var log in result.EnumerateArray())
        {
            if (!MatchesContract(log, "address", contractAddress))
            {
                continue;
            }
            var txHash = log.GetProperty("transactionHash").GetString()!.ToLowerInvariant();
            var blockNumber = Convert.ToInt64(log.GetProperty("blockNumber").GetString()![2..], 16);
            if (ParseRpcTransfer(log, txHash, blockNumber) is { } transfer)
            {
                logs.Add(transfer);
            }
        }

        var resultCount = result.GetArrayLength();
        var complete = resultCount < TruncateRatio * _nodeLogResultCap;
        if (!complete)
        {
            _logger.LogWarning(
                "getLogs 返回 {Count} 条达到上限八成（cap={Cap}），区间 {From}..{To} 视为未读完",
                resultCount, _nodeLogResultCap, fromBlock, toBlock);
        }

        return (logs, complete);
    }

    public async Task<AddressTransferPage> GetTransfersForAddressAsync(
        string contractAddress, string address, long fromBlock, long toBlock,
        int page, int pageSize, CancellationToken ct = default)
    {
        if (_etherscanEndpoints.Length == 0)
        {
            throw new ChainRpcException("Etherscan 查询不可用，请配置启用的节点密钥。");
        }

        // 按地址和区块区间补查转账，仍需用日志索引与 RPC 扫描结果去重。
        var query = $"&contractaddress={contractAddress}&address={address}" +
                    $"&startblock={fromBlock}&endblock={toBlock}" +
                    $"&page={page}&offset={pageSize}&sort=asc";
        for (var attempt = 0; attempt < _etherscanEndpoints.Length; attempt++)
        {
            var idx = (_etherscanStart + attempt) % _etherscanEndpoints.Length;
            using var resp = await _http.GetAsync(BuildEtherscanUrl(_etherscanEndpoints[idx], query), ct);
            if (IsKeyWorthRotating(resp.StatusCode))
            {
                if (attempt < _etherscanEndpoints.Length - 1)
                {
                    _logger.LogWarning("Etherscan key#{Idx} HTTP {Status}，切下一把", idx, (int)resp.StatusCode);
                    continue;
                }
            }

            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var status = root.GetProperty("status").GetString();
            var items = root.GetProperty("result");
            if (status != "1")
            {
                var reason = root.TryGetProperty("message", out var m) ? m.GetString() : null;
                if (root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String)
                {
                    reason += " " + r.GetString();
                }

                // 配额/认证类（"Max rate limit reached"/"Invalid API Key"…）→ 换 key 再试
                if (IsQuotaOrAuthMessage(reason) && attempt < _etherscanEndpoints.Length - 1)
                {
                    _logger.LogWarning("Etherscan key#{Idx} 额度不足或认证失败，切下一把", idx);
                    continue;
                }

                // status=0 且 message=No transactions found 不是错误
                if (items.ValueKind == JsonValueKind.Array && items.GetArrayLength() == 0
                    && string.Equals(reason, "No transactions found", StringComparison.OrdinalIgnoreCase))
                {
                    return new AddressTransferPage(Array.Empty<TokenTransferLog>(), false);
                }

                throw new ChainRpcException("Etherscan 拒绝了转账记录查询。");
            }

            return await ParseEtherscanTransfersAsync(items, contractAddress, address, fromBlock, toBlock, pageSize, ct);
        }

        // 循环必然 return/throw（末次尝试不再 continue）；到达这里说明逻辑有洞，显式失败
        throw new ChainRpcException("etherscan-v2: 轮换循环意外退出");
    }

    private async Task<AddressTransferPage> ParseEtherscanTransfersAsync(
        JsonElement items, string contractAddress, string address, long fromBlock, long toBlock,
        int pageSize, CancellationToken ct)
    {
        var logs = new Dictionary<(string TxHash, int LogIndex), TokenTransferLog>();
        var receiptHashes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in items.EnumerateArray())
        {
            var txHash = t.GetProperty("hash").GetString()!.ToLowerInvariant();
            // tokentx 可能不返回 logIndex。同一交易可发出多条 Transfer，transactionIndex 无法替代日志索引。
            if (!MatchesContract(t, "contractAddress", contractAddress)
                || !t.TryGetProperty("logIndex", out var index) || !TryReadLogIndex(index, out var logIndex))
            {
                receiptHashes.Add(txHash);
                continue;
            }

            logs[(txHash, logIndex)] = new TokenTransferLog(txHash, logIndex,
                t.GetProperty("from").GetString()!, t.GetProperty("to").GetString()!,
                t.GetProperty("value").GetString()!, long.Parse(t.GetProperty("blockNumber").GetString()!));
        }

        var unresolved = false;
        foreach (var hash in receiptHashes)
        {
            var transfers = await ReadReceiptTransfersAsync(hash, contractAddress, address, fromBlock, toBlock, ct);
            if (transfers.Count == 0)
            {
                unresolved = true;
                continue;
            }

            foreach (var transfer in transfers)
            {
                logs[(transfer.TxHash, transfer.LogIndex)] = transfer;
            }
        }

        return new AddressTransferPage(logs.Values.ToList(), items.GetArrayLength() >= pageSize, unresolved);
    }

    private async Task<IReadOnlyList<TokenTransferLog>> ReadReceiptTransfersAsync(
        string hash, string contractAddress, string address, long fromBlock, long toBlock, CancellationToken ct)
    {
        var (logs, _) = await ReadReceiptTransfersCoreAsync(hash, contractAddress, fromBlock, toBlock, ct);
        return logs.Where(transfer => string.Equals(transfer.From, address, StringComparison.OrdinalIgnoreCase)
            || string.Equals(transfer.To, address, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    public Task<(IReadOnlyList<TokenTransferLog> Logs, bool Complete)> GetReceiptTransferLogsAsync(
        string txHash, string contractAddress, long block, CancellationToken ct = default)
        => ReadReceiptTransfersCoreAsync(txHash, contractAddress, block, block, ct);

    /// <summary>同一条回执解析路径供归集证明和地址兜底使用，空成功回执与证据未读到分别返回。</summary>
    private async Task<(IReadOnlyList<TokenTransferLog> Logs, bool Complete)> ReadReceiptTransfersCoreAsync(
        string hash, string contractAddress, long fromBlock, long toBlock, CancellationToken ct)
    {
        try
        {
            var result = await RpcAsync("eth_getTransactionReceipt", new object[] { hash }, ct);
            if (result is not { ValueKind: JsonValueKind.Object } receipt
                || !string.Equals(receipt.GetProperty("transactionHash").GetString(), hash, StringComparison.OrdinalIgnoreCase)
                || receipt.GetProperty("status").GetString() != "0x1")
            {
                return (Array.Empty<TokenTransferLog>(), false);
            }

            var block = Convert.ToInt64(receipt.GetProperty("blockNumber").GetString()![2..], 16);
            if (block < fromBlock || block > toBlock)
            {
                return (Array.Empty<TokenTransferLog>(), false);
            }

            var transfers = new List<TokenTransferLog>();
            foreach (var log in receipt.GetProperty("logs").EnumerateArray())
            {
                if (MatchesContract(log, "address", contractAddress)
                    && ParseRpcTransfer(log, hash, block) is { } transfer)
                {
                    transfers.Add(transfer);
                }
            }

            return (transfers, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or ChainRpcException or JsonException
            or KeyNotFoundException or InvalidOperationException or FormatException or OverflowException or ArgumentOutOfRangeException)
        {
            _logger.LogWarning("交易 {TxHash} 的回执无法确认 Transfer 日志索引：{ErrorType}", hash, ex.GetType().Name);
            return (Array.Empty<TokenTransferLog>(), false);
        }
    }

    private static TokenTransferLog? ParseRpcTransfer(JsonElement log, string hash, long block)
    {
        if (log.TryGetProperty("removed", out var removed) && removed.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        var topics = log.GetProperty("topics");
        if (topics.GetArrayLength() < 3 || !string.Equals(topics[0].GetString(), TransferTopic, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!TryReadLogIndex(log.GetProperty("logIndex"), out var logIndex))
        {
            throw new FormatException("Transfer 日志缺少有效的 logIndex。");
        }

        var from = ToAddress(topics[1].GetString()!);
        var to = ToAddress(topics[2].GetString()!);
        var amount = HexToDecimalString(log.GetProperty("data").GetString()!);
        return new TokenTransferLog(hash, logIndex, from, to, amount, block);
    }

    private static bool MatchesContract(JsonElement log, string field, string contractAddress) =>
        log.TryGetProperty(field, out var address) && address.ValueKind == JsonValueKind.String
            && string.Equals(address.GetString(), contractAddress, StringComparison.OrdinalIgnoreCase);

    private static bool TryReadLogIndex(JsonElement value, out int index)
    {
        if (value.ValueKind == JsonValueKind.Number)
        {
            return value.TryGetInt32(out index) && index >= 0;
        }

        index = -1;
        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { Length: > 0 } text)
        {
            return false;
        }

        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? int.TryParse(text.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out index) && index >= 0
            : int.TryParse(text, System.Globalization.NumberStyles.None, null, out index);
    }

    public async Task<string> GetTokenBalanceRawAsync(string contractAddress, string address, CancellationToken ct = default)
    {
        // balanceOf(address) 选择子 0x70a08231 + pad32(address)
        var data = "0x70a08231" + address.ToLowerInvariant().TrimStart('0', 'x').PadLeft(64, '0');
        var result = await RpcAsync("eth_call", new object[] { new Dictionary<string, string> { ["to"] = contractAddress, ["data"] = data }, "latest" }, ct);
        return HexToDecimalString(result!.Value.GetString()!);
    }

    public async Task<decimal> GetBnbBalanceAsync(string address, CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_getBalance", new object[] { address, "latest" }, ct);
        return HexWeiToEther(result!.Value.GetString()!);
    }

    public async Task<long> GetTransactionCountAsync(string address, CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_getTransactionCount", new object[] { address, "pending" }, ct);
        return Convert.ToInt64(result!.Value.GetString()!, 16);
    }

    public async Task<TransactionReceipt> GetTransactionReceiptAsync(string txHash, CancellationToken ct = default)
    {
        JsonElement? result;
        try
        {
            result = await RpcAsync("eth_getTransactionReceipt", new object[] { txHash }, ct);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new ChainRpcException("节点返回的交易回执 JSON 无效。");
        }

        if (result is { ValueKind: JsonValueKind.Null })
        {
            return new TransactionReceipt(txHash, null, null);
        }

        if (result is not { ValueKind: JsonValueKind.Object } value
            || !MatchesContract(value, "transactionHash", txHash)
            || !TryReadReceiptQuantity(value, "blockNumber", out var block)
            || !TryReadReceiptQuantity(value, "status", out var status)
            || status is not (0 or 1))
        {
            throw new ChainRpcException("节点返回的交易回执缺少有效的哈希、区块号或状态。");
        }

        return new TransactionReceipt(txHash, block, (int)status);
    }

    private static bool TryReadReceiptQuantity(JsonElement receipt, string field, out long quantity)
    {
        quantity = -1;
        return receipt.TryGetProperty(field, out var value)
               && value.ValueKind == JsonValueKind.String
               && value.GetString() is { Length: > 2 } hex
               && hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
               && long.TryParse(hex.AsSpan(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out quantity)
               && quantity >= 0;
    }

    public Task<long?> GetTransactionNonceAsync(string txHash, CancellationToken ct = default)
        => ReadTransactionNonceAsync(txHash, null, ct);

    public Task<long?> GetTransactionNonceForSenderAsync(string txHash, string sender, CancellationToken ct = default)
        => ReadTransactionNonceAsync(txHash, sender, ct);

    private async Task<long?> ReadTransactionNonceAsync(string txHash, string? expectedSender, CancellationToken ct)
    {
        var result = await RpcAsync("eth_getTransactionByHash", new object[] { txHash }, ct);
        if (result is null || result.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (expectedSender != null && (!result.Value.TryGetProperty("from", out var from)
                                       || !string.Equals(from.GetString(), expectedSender, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var nonceHex = result.Value.TryGetProperty("nonce", out var nonce) ? nonce.GetString() : null;
        return nonceHex is null ? null : Convert.ToInt64(nonceHex.Substring(2), 16);
    }

    public async Task<string> SendTransactionAsync(
        string privateKey, string to, string dataHex, string valueWei, long chainId, long nonce,
        CancellationToken ct = default)
    {
        var transaction = await PrepareTransactionAsync(privateKey, to, dataHex, valueWei, chainId, nonce, ct);
        await BroadcastTransactionAsync(transaction, ct);
        return transaction.Hash;
    }

    public async Task<SignedChainTransaction> PrepareTransactionAsync(
        string privateKey, string to, string dataHex, string valueWei, long chainId, long nonce,
        CancellationToken ct = default)
    {
        var gasPriceHex = (await RpcAsync("eth_gasPrice", Array.Empty<object>(), ct))!.Value.GetString()!;

        // 原生转账使用 21,000 gas，带调用数据的代币交易预留 100,000 gas。
        var signer = new Nethereum.Signer.LegacyTransactionSigner();
        var keyBytes = new Nethereum.Signer.EthECKey(privateKey).GetPrivateKeyAsBytes();
        var raw = signer.SignTransaction(
            keyBytes,
            new System.Numerics.BigInteger(chainId),
            to,
            HexStringToBigInteger(valueWei),
            new System.Numerics.BigInteger(nonce),
            HexStringToBigInteger(gasPriceHex),
            HexStringToBigInteger(string.IsNullOrEmpty(dataHex) ? "0x5208" : "0x186A0"),
            dataHex ?? "");

        var rawHex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw[2..] : raw;
        var hash = Nethereum.Util.Sha3Keccack.Current.CalculateHash(Convert.FromHexString(rawHex));
        return new SignedChainTransaction("0x" + Convert.ToHexStringLower(hash), "0x" + rawHex);
    }

    public async Task BroadcastTransactionAsync(SignedChainTransaction transaction, CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_sendRawTransaction", new object[] { transaction.RawHex }, ct);
        if (result is not { ValueKind: JsonValueKind.String } hash
            || !string.Equals(hash.GetString(), transaction.Hash, StringComparison.OrdinalIgnoreCase))
        {
            throw new ChainRpcException("节点未确认已签名交易的哈希，发送结果需通过回执核对。");
        }
    }

    public async Task<decimal> GetGasPriceGweiAsync(CancellationToken ct = default)
    {
        var result = await RpcAsync("eth_gasPrice", Array.Empty<object>(), ct);
        return HexWeiToEther(result!.Value.GetString()!) * 1_000_000_000m;
    }

    // ── JSON-RPC 通道（多 key 轮换）───────────────────────────────────────────────────────

    /// <summary>从起始偏移开始逐个端点尝试；配额/认证类错误切下一把，其它错误原样上抛。</summary>
    private async Task<JsonElement?> RpcAsync(string method, object[] parameters, CancellationToken ct)
    {
        for (var attempt = 0; attempt < _rpcEndpoints.Length; attempt++)
        {
            var idx = (_rpcStart + attempt) % _rpcEndpoints.Length;
            try
            {
                return await RpcOnceAsync(_rpcEndpoints[idx], method, parameters, ct);
            }
            catch (Exception ex) when (IsKeyWorthRotating(ex) && attempt < _rpcEndpoints.Length - 1)
            {
                // 只记录 key 序号不打 URL——URL 里含项目密钥
                _logger.LogWarning("RPC key#{Idx} 调 {Method} 时额度不足或认证失败，切下一把", idx, method);
            }
        }

        throw new InvalidOperationException("unreachable: 轮换循环必然 return 或在末次尝试时上抛");
    }

    private async Task<JsonElement?> RpcOnceAsync(string endpoint, string method, object[] parameters, CancellationToken ct)
    {
        using var content = new StringContent(
            JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = method,
                ["params"] = parameters,
            }),
            Encoding.UTF8, "application/json");
        using var resp = await _http.PostAsync(endpoint, content, ct);
        if (IsKeyWorthRotating(resp.StatusCode))
        {
            // 带状态码的 HttpRequestException——轮换过滤器按 StatusCode 识别（EnsureSuccessStatusCode 同款）
            throw new HttpRequestException($"{method}: HTTP {(int)resp.StatusCode}", null, resp.StatusCode);
        }

        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
        {
            var isKeyError = error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String
                && IsQuotaOrAuthMessage(message.GetString());
            var code = error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("code", out var value) && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var number) ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : "未知";
            throw new ChainRpcException($"{method}: 节点拒绝请求（错误码 {code}）。", isKeyError);
        }

        // 返回值需要独立于即将释放的 JsonDocument。
        return root.TryGetProperty("result", out var result) ? result.Clone() : null;
    }

    // ── key 轮换判定 ─────────────────────────────────────────────────────────────────────

    /// <summary>HTTP 层配额/认证状态：值得换一把 key 再试（StatusCode 为 nullable，null=不切换）。</summary>
    internal static bool IsKeyWorthRotating(HttpStatusCode? statusCode)
    {
        return statusCode is HttpStatusCode.Unauthorized        // 401 key 无效
            or HttpStatusCode.Forbidden                          // 403 被禁
            or HttpStatusCode.TooManyRequests;                   // 429 限流
    }

    /// <summary>异常是否「换 key 有意义」：HTTP 401/403/429，或节点/浏览器返回的配额认证类消息。</summary>
    internal static bool IsKeyWorthRotating(Exception ex)
    {
        return ex is HttpRequestException hre && IsKeyWorthRotating(hre.StatusCode)
               || ex is ChainRpcException { IsKeyError: true };
    }

    /// <summary>JSON-RPC/浏览器层错误消息是否配额或认证类（英文消息，大小写不敏感）。</summary>
    internal static bool IsQuotaOrAuthMessage(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return false;
        }

        return message.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
               || message.Contains("too many requests", StringComparison.OrdinalIgnoreCase)
               || message.Contains("quota", StringComparison.OrdinalIgnoreCase)
               || message.Contains("credit", StringComparison.OrdinalIgnoreCase)
               || message.Contains("unauthorized", StringComparison.OrdinalIgnoreCase)
               || message.Contains("forbidden", StringComparison.OrdinalIgnoreCase)
               || message.Contains("invalid api key", StringComparison.OrdinalIgnoreCase)
               || message.Contains("missing api key", StringComparison.OrdinalIgnoreCase);
    }

    // ── 规整工具 ─────────────────────────────────────────────────────────────────────────

    internal static string ToHex(long value) => "0x" + value.ToString("x");

    /// <summary>"0x.." hex → BigInteger（uint256 数值，非负）。</summary>
    internal static System.Numerics.BigInteger HexStringToBigInteger(string hex)
    {
        var digits = hex.StartsWith("0x") ? hex[2..] : hex;
        if (digits.Length == 0)
        {
            return System.Numerics.BigInteger.Zero;
        }

        return System.Numerics.BigInteger.Parse("0" + digits, System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string ToAddress(string topic) => "0x" + topic[^40..];

    /// <summary>32 字节 hex 整数 → 十进制字符串（uint256，任意长度，不经过 double/long）。</summary>
    internal static string HexToDecimalString(string hex)
    {
        var digits = hex.StartsWith("0x") ? hex[2..] : hex;
        if (digits.Length == 0)
        {
            return "0";
        }

        // 大数转十进制：按字节进位（uint256 最多 78 位十进制）
        var bytes = new byte[digits.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = Convert.ToByte(digits.Substring(i * 2, 2), 16);
        }

        var result = new List<byte> { 0 }; // 反序的十进制位
        foreach (var b in bytes)
        {
            var carry = (int)b;
            for (var i = 0; i < result.Count; i++)
            {
                carry += result[i] << 8;
                result[i] = (byte)(carry % 10);
                carry /= 10;
            }

            while (carry > 0)
            {
                result.Add((byte)(carry % 10));
                carry /= 10;
            }
        }

        result.Reverse();
        return new string(result.SkipWhile(b => b == 0 && result.Count > 1).Select(b => (char)(b + '0')).ToArray());
    }

    internal static decimal HexWeiToEther(string hex)
    {
        var raw = HexToDecimalString(hex);
        return decimal.Parse(raw) / 1_000_000_000_000_000_000m;
    }
}

/// <summary>节点报错（getLogs 限流/超时等）——按游标纪律当「没读完」处理。</summary>
public class ChainRpcException : Exception
{
    public ChainRpcException(string message) : this(message, isKeyError: false)
    {
    }

    internal ChainRpcException(string message, bool isKeyError) : base(message)
    {
        IsKeyError = isKeyError;
    }

    internal bool IsKeyError { get; }
}
