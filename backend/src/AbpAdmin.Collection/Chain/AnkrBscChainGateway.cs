using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// Ankr BSC JSON-RPC 网关（https://rpc.ankr.com/bsc/&#123;project-key&#125;，测量见 docs/collection-chain-runbook）。
/// getLogs 按 5.6 节口径判「没读完」：节点报错，或条数 ≥ 上限八成（上限由实测得来，进配置）。
/// 签名广播用 Nethereum 的 LegacyTransaction（EIP-155），JSON-RPC 走本类自写——网关薄、假实现可替。
/// 多 KEY：同一服务商可挂多把（管理页维护），遇 429/401/403/配额类错误自动切下一把重试；
/// 其它错误（getLogs 超限、执行回退等）不切——那不是 key 的问题，切了纯浪费且破坏游标纪律。
/// </summary>
public class AnkrBscChainGateway : IBscChainGateway
{
    /// <summary>keccak256("Transfer(address,address,uint256)")。</summary>
    public const string TransferTopic = "0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef";

    /// <summary>截断判定的安全系数：返回条数 ≥ Cap×0.8 视为可能没读完（计划第 5.6 节）。</summary>
    public const double TruncateRatio = 0.8;

    /// <summary>Ankr BSC RPC URL 模板（key 池的拼接基座；换服务商=改代码，参考项目同口径）。</summary>
    public const string AnkrBscRpcUrlBase = "https://rpc.ankr.com/bsc/";

    /// <summary>Etherscan V2 端点里 key 参数名（从配置端点截取基座，chainid 等参数跟配置走）。</summary>
    public const string EtherscanApiKeyParam = "apikey=";

    private readonly HttpClient _http;
    private readonly ILogger<AnkrBscChainGateway> _logger;
    private readonly string[] _rpcEndpoints;
    private readonly string[] _etherscanEndpoints;
    private readonly int _nodeLogResultCap;
    private readonly int _rpcStart;
    private readonly int _etherscanStart;

    /// <summary>端点数组各 ≥1；起始偏移由工厂按轮递增（同进程跨轮轮着用不同 key 起步）。</summary>
    public AnkrBscChainGateway(
        HttpClient http, string[] rpcEndpoints, string[] etherscanEndpoints, int nodeLogResultCap,
        int rpcStartOffset, int etherscanStartOffset, ILogger<AnkrBscChainGateway> logger)
    {
        if (rpcEndpoints.Length == 0 || etherscanEndpoints.Length == 0)
        {
            throw new ArgumentException("RPC/Etherscan 端点至少各一把");
        }

        _http = http;
        _rpcEndpoints = rpcEndpoints;
        _etherscanEndpoints = etherscanEndpoints;
        _nodeLogResultCap = nodeLogResultCap;
        _rpcStart = ((rpcStartOffset % rpcEndpoints.Length) + rpcEndpoints.Length) % rpcEndpoints.Length;
        _etherscanStart = ((etherscanStartOffset % etherscanEndpoints.Length) + etherscanEndpoints.Length) % etherscanEndpoints.Length;
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
            result = (await RpcAsync("eth_getLogs", new object[] { parameters }, ct)).Value;
        }
        catch (Exception ex) when (ex is ChainRpcException || ex is InvalidOperationException)
        {
            // 节点报错 / 响应体超限（宿主 5MB 报文透视上限，真链 E2E 抓到）都按「没读完」处理：
            // 缩小区间重试是调用方的游标纪律（计划第 5.6 节）
            _logger.LogWarning(ex, "getLogs 未读完（{From}..{To}）", fromBlock, toBlock);
            return (Array.Empty<TokenTransferLog>(), false);
        }

        var logs = new List<TokenTransferLog>();
        foreach (var log in result.EnumerateArray())
        {
            // 非白名单合约已被 address 过滤； topics[1]/[2] 是 from/to（pad 到 32 字节）
            var txHash = log.GetProperty("transactionHash").GetString()!;
            var logIndex = Convert.ToInt32(log.GetProperty("logIndex").GetString()!.Substring(2), 16);
            var blockNumber = Convert.ToInt64(log.GetProperty("blockNumber").GetString()!.Substring(2), 16);
            var topics = log.GetProperty("topics");
            if (topics.GetArrayLength() < 3)
            {
                continue;
            }

            var from = ToAddress(topics[1].GetString()!);
            var to = ToAddress(topics[2].GetString()!);
            var raw = HexToDecimalString(log.GetProperty("data").GetString()!);
            logs.Add(new TokenTransferLog(txHash.ToLowerInvariant(), logIndex, from, to, raw, blockNumber));
        }

        var complete = logs.Count < TruncateRatio * _nodeLogResultCap;
        if (!complete)
        {
            _logger.LogWarning(
                "getLogs 返回 {Count} 条达到上限八成（cap={Cap}），区间 {From}..{To} 视为未读完",
                logs.Count, _nodeLogResultCap, fromBlock, toBlock);
        }

        return (logs, complete);
    }

    public async Task<AddressTransferPage> GetTransfersForAddressAsync(
        string contractAddress, string address, long fromBlock, long toBlock,
        int page, int pageSize, CancellationToken ct = default)
    {
        // Etherscan V2 多链端点（chainid=56）；tokentx 支持区块区间，做 1 块读不完时的按地址兜底。
        // 多 key 轮换：免费档 5 req/s 最容易限流，命中配额/认证类错误就换下一把再试
        var query = $"&contractaddress={contractAddress}&address={address}" +
                    $"&startblock={fromBlock}&endblock={toBlock}" +
                    $"&page={page}&offset={pageSize}&sort=asc";
        for (var attempt = 0; attempt < _etherscanEndpoints.Length; attempt++)
        {
            var idx = (_etherscanStart + attempt) % _etherscanEndpoints.Length;
            using var resp = await _http.GetAsync(_etherscanEndpoints[idx] + query, ct);
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
                    _logger.LogWarning("Etherscan key#{Idx} 被拒（{Reason}），切下一把", idx, reason);
                    continue;
                }

                // status=0 且 message=No transactions found 不是错误
                if (items.ValueKind == JsonValueKind.Array)
                {
                    return new AddressTransferPage(Array.Empty<TokenTransferLog>(), false);
                }

                throw new ChainRpcException("etherscan-v2: " + reason);
            }

            return ParseEtherscanTransfers(items, pageSize);
        }

        // 循环必然 return/throw（末次尝试不再 continue）；到达这里说明逻辑有洞，显式失败
        throw new ChainRpcException("etherscan-v2: 轮换循环意外退出");
    }

    private AddressTransferPage ParseEtherscanTransfers(JsonElement items, int pageSize)
    {
        var logs = new List<TokenTransferLog>();
        foreach (var t in items.EnumerateArray())
        {
            if (!string.Equals(t.GetProperty("tokenSymbol").GetString(), "ERC20", StringComparison.OrdinalIgnoreCase)
                && t.GetProperty("value").GetString() == null)
            {
                continue;
            }

            logs.Add(new TokenTransferLog(
                t.GetProperty("hash").GetString()!.ToLowerInvariant(),
                int.Parse(t.GetProperty("transactionIndex").GetString()!),
                t.GetProperty("from").GetString()!,
                t.GetProperty("to").GetString()!,
                t.GetProperty("value").GetString()!,
                long.Parse(t.GetProperty("blockNumber").GetString()!)));
        }

        return new AddressTransferPage(logs, logs.Count >= pageSize);
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
        var result = await RpcAsync("eth_getTransactionReceipt", new object[] { txHash }, ct);
        if (result.Value.ValueKind == JsonValueKind.Null)
        {
            return new TransactionReceipt(txHash, null, null);
        }

        return new TransactionReceipt(
            txHash,
            Convert.ToInt64(result.Value.GetProperty("blockNumber").GetString()!.Substring(2), 16),
            Convert.ToInt32(result.Value.GetProperty("status").GetString()!.Substring(2), 16));
    }

    public async Task<string> SendTransactionAsync(
        string privateKey, string to, string dataHex, string valueWei, long chainId, long nonce,
        CancellationToken ct = default)
    {
        var gasPriceHex = (await RpcAsync("eth_gasPrice", Array.Empty<object>(), ct))!.Value.GetString()!;

        // gas 21000 只够裸转账；ERC20 transfer 链上 ~5 万，给 0x186A0。
        // EIP-155：SignTransaction(byte[] key, BigInteger chainId, ...) 返回签名后 RLP hex
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

        var hash = await RpcAsync("eth_sendRawTransaction", new object[] { raw.StartsWith("0x") ? raw : "0x" + raw }, ct);
        return hash!.Value.GetString()!.ToLowerInvariant();
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
                _logger.LogWarning("RPC key#{Idx} 调 {Method} 被拒（{Reason}），切下一把",
                    idx, method, ex.Message);
            }
        }

        throw new InvalidOperationException("unreachable: 轮换循环必然 return 或在末次尝试时上抛");
    }

    private async Task<JsonElement?> RpcOnceAsync(string endpoint, string method, object[] parameters, CancellationToken ct)
    {
        // JSON-RPC 2.0 参数字段名是 params（匿名类型不能叫 params，用字典）——
        // 写成 parameters 时无参方法侥幸能通、带参方法全部 "No more params"（真链 E2E 抓到）
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
            var message = error.GetProperty("message").GetString();
            if (IsQuotaOrAuthMessage(message))
            {
                throw new ChainRpcException(message!);
            }

            throw new ChainRpcException(method + ": " + message);
        }

        // Clone 脱离已释放的 JsonDocument（using 退出后 pooled buffer 失效，实测 E2E 抓到
        // ObjectDisposedException）——克隆是独立拷贝
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
               || ex is ChainRpcException cre && IsQuotaOrAuthMessage(cre.Message);
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

        return System.Numerics.BigInteger.Parse(digits, System.Globalization.NumberStyles.HexNumber,
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
    public ChainRpcException(string message) : base(message)
    {
    }
}
