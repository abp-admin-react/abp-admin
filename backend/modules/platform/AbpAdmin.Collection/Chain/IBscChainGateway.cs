using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace AbpAdmin.Collection.Chain;

/// <summary>一条 ERC20 Transfer 日志（已从十六进制规整为十进制/整数）。</summary>
public record TokenTransferLog(
    string TxHash,
    int LogIndex,
    string From,
    string To,
    string RawAmount,
    long BlockNumber);

/// <summary>交易回执（归集确认与重签判据）。</summary>
public record TransactionReceipt(
    string TxHash,
    /// <summary>null = 尚无回执（还在池里/被丢弃）。</summary>
    long? BlockNumber,
    /// <summary>null = 无回执；1 = 成功；0 = 失败（revert）。</summary>
    int? Status,
    BigInteger? GasUsed = null,
    BigInteger? EffectiveGasPriceWei = null);

/// <summary>本地签名后的交易。哈希可在发送之前持久化，RawHex 仅用于提交同一份签名数据。</summary>
public sealed record SignedChainTransaction(
    string Hash,
    string RawHex,
    BigInteger? GasPriceWei = null,
    int? GasLimit = null);

/// <summary>eth_call 预模拟结果。</summary>
public sealed record ChainCallSimulationResult(bool Success, bool Reverted, string? ErrorMessage)
{
    public static readonly ChainCallSimulationResult Succeeded = new(true, false, null);
    public static ChainCallSimulationResult Revert(string? error) => new(false, true, error);
    public static ChainCallSimulationResult Indeterminate(string? error) => new(false, false, error);
}

/// <summary>按地址分批查询的一次结果（块区间无法整读时的兜底通道）。</summary>
public record AddressTransferPage(
    IReadOnlyList<TokenTransferLog> Logs,
    bool Incomplete,
    bool HasUnresolvedLogs = false);

/// <summary>
/// BSC 链上查询、签名和广播。按地址补查 Transfer 日志时可使用 Etherscan V2。
/// </summary>
public interface IBscChainGateway
{
    Task<long> GetLatestBlockNumberAsync(CancellationToken ct = default);

    /// <summary>
    /// 拉区间内该合约的 Transfer 日志。返回 false = 响应可能被截断（条数达到上限八成或节点报错）：
    /// 调用方按游标规则缩小区间重试，不得前进。
    /// </summary>
    Task<(IReadOnlyList<TokenTransferLog> Logs, bool Complete)> GetTransferLogsAsync(
        string contractAddress, long fromBlock, long toBlock, CancellationToken ct = default);

    /// <summary>按合约 + 可选发送/接收地址过滤拉取区间内 Transfer 日志（供归集发现与热钱包监控）。</summary>
    async Task<(IReadOnlyList<TokenTransferLog> Logs, bool Complete)> GetFilteredTransferLogsAsync(
        string contractAddress, string? fromAddress, string? toAddress, long fromBlock, long toBlock,
        CancellationToken ct = default)
    {
        var (logs, complete) = await GetTransferLogsAsync(contractAddress, fromBlock, toBlock, ct);
        var filtered = logs.Where(l =>
                (fromAddress == null || string.Equals(l.From, fromAddress, StringComparison.OrdinalIgnoreCase))
                && (toAddress == null || string.Equals(l.To, toAddress, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return (filtered, complete);
    }

    /// <summary>按地址分批兜底。Incomplete 表示还有下一页；HasUnresolvedLogs 表示本页有日志未能确认。</summary>
    Task<AddressTransferPage> GetTransfersForAddressAsync(
        string contractAddress, string address, long fromBlock, long toBlock,
        int page, int pageSize, CancellationToken ct = default);

    /// <summary>balanceOf，返回原始整数十进制字符串。</summary>
    Task<string> GetTokenBalanceRawAsync(string contractAddress, string address, CancellationToken ct = default);

    /// <summary>读取指定区块的 ERC20 balanceOf。金库核对不能回退到 latest。</summary>
    Task<string> GetTokenBalanceRawAtBlockAsync(string contractAddress, string address, long block, CancellationToken ct = default);

    /// <summary>BNB 余额（ether 十进制）。</summary>
    Task<decimal> GetBnbBalanceAsync(string address, CancellationToken ct = default);

    Task<long> GetTransactionCountAsync(string address, CancellationToken ct = default);

    /// <summary>读取账户在 latest 块的已确认 nonce（用于避免负载均衡 RPC pending 视图漂移）。</summary>
    Task<long> GetTransactionCountLatestAsync(string address, CancellationToken ct = default)
        => GetTransactionCountAsync(address, ct);

    /// <summary>读取账户在指定块的交易数，供打油恢复与共址提现核对已消耗的 nonce。</summary>
    Task<long?> GetTransactionCountAtBlockAsync(string address, long block, CancellationToken ct = default)
        => Task.FromResult<long?>(null);

    /// <summary>回执；无回执返回 BlockNumber/Status 为 null 的记录。</summary>
    Task<TransactionReceipt> GetTransactionReceiptAsync(string txHash, CancellationToken ct = default);

    /// <summary>获取指定回执的代币转账。Complete=false 表示节点尚未提供可核对的证据。</summary>
    Task<(IReadOnlyList<TokenTransferLog> Logs, bool Complete)> GetReceiptTransferLogsAsync(
        string txHash, string contractAddress, long block, CancellationToken ct = default)
        => GetTransferLogsAsync(contractAddress, block, block, ct);

    /// <summary>
    /// 交易的 nonce（eth_getTransactionByHash）；交易不存在或节点不给 nonce 返回 null。
    /// </summary>
    Task<long?> GetTransactionNonceAsync(string txHash, CancellationToken ct = default);

    /// <summary>仅当链上交易确由指定发送地址发出时返回 nonce。</summary>
    Task<long?> GetTransactionNonceForSenderAsync(string txHash, string sender, CancellationToken ct = default)
        => Task.FromResult<long?>(null);

    /// <summary>查询节点内存池/链上交易的 gasPrice（wei）；交易不存在返回 null。</summary>
    Task<BigInteger?> GetTransactionGasPriceWeiAsync(string txHash, CancellationToken ct = default)
        => Task.FromResult<BigInteger?>(null);

    /// <summary>通过 eth_call 模拟执行调用；在打油前拦截必 revert 的合约转账。</summary>
    Task<ChainCallSimulationResult> SimulateCallAsync(
        string from, string to, string dataHex, CancellationToken ct = default)
        => Task.FromResult(ChainCallSimulationResult.Succeeded);

    /// <summary>签名 EIP-155 交易并计算完整签名数据的哈希，不发送交易。</summary>
    Task<SignedChainTransaction> PrepareTransactionAsync(
        string privateKey, string to, string dataHex, string valueWei, long chainId, long nonce,
        CancellationToken ct = default);

    /// <summary>使用显式 gasPrice（wei）签名 EIP-155 交易，供抬价重签与同 nonce 替换复用。</summary>
    Task<SignedChainTransaction> PrepareTransactionAsync(
        string privateKey, string to, string dataHex, string valueWei, long chainId, long nonce,
        BigInteger? gasPriceWei, CancellationToken ct = default)
        => PrepareTransactionAsync(privateKey, to, dataHex, valueWei, chainId, nonce, ct);

    /// <summary>发送已签名的交易。调用方可在调用前保存该交易的哈希。</summary>
    Task BroadcastTransactionAsync(SignedChainTransaction transaction, CancellationToken ct = default);

    /// <summary>
    /// 签名并广播一笔 EIP-155 legacy 交易。
    /// </summary>
    Task<string> SendTransactionAsync(
        string privateKey, string to, string dataHex, string valueWei, long chainId, long nonce,
        CancellationToken ct = default);

    /// <summary>使用显式 gasPrice（wei）签名并广播一笔 EIP-155 legacy 交易。</summary>
    async Task<string> SendTransactionAsync(
        string privateKey, string to, string dataHex, string valueWei, long chainId, long nonce,
        BigInteger? gasPriceWei, CancellationToken ct = default)
    {
        var signed = await PrepareTransactionAsync(privateKey, to, dataHex, valueWei, chainId, nonce, gasPriceWei, ct);
        await BroadcastTransactionAsync(signed, ct);
        return signed.Hash;
    }

    Task<decimal> GetGasPriceGweiAsync(CancellationToken ct = default);

    /// <summary>读取当前节点 eth_gasPrice（wei 精确整数，避免小数值乘除截断）。</summary>
    async Task<BigInteger> GetGasPriceWeiAsync(CancellationToken ct = default)
        => BscGasPricePolicy.GweiToWei(await GetGasPriceGweiAsync(ct));

    /// <summary>读取地址链上字节码（eth_getCode）；纯 EOA 返回 "0x"，非空提示合约或 EIP-7702 委托。</summary>
    Task<string> GetCodeAsync(string address, CancellationToken ct = default)
        => Task.FromResult("0x");

    /// <summary>
    /// 复核地址是否确实存在非空字节码（多节点共识或双次独立复核），避免单节点畸形/异常响应误熔断。
    /// Confirmed=true 表示确认命中非空合法字节码；Disagreement=true 表示二次复核结果不一致。
    /// </summary>
    async Task<(bool Confirmed, string Code, bool Disagreement)> VerifyDeployedCodeAsync(
        string address, CancellationToken ct = default)
    {
        var first = await GetCodeAsync(address, ct);
        if (!BscGasPricePolicy.HasDeployedCode(first))
        {
            return (false, first, false);
        }

        var second = await GetCodeAsync(address, ct);
        if (BscGasPricePolicy.HasDeployedCode(second))
        {
            return (true, first, false);
        }

        return (false, first, true);
    }
}
