using System.Collections.Generic;
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
    int? Status);

/// <summary>按地址分批查询的一次结果（块区间无法整读时的兜底通道）。</summary>
public record AddressTransferPage(IReadOnlyList<TokenTransferLog> Logs, bool Incomplete);

/// <summary>
/// BSC 链网关（计划第 5.6 节）：只读操作（getLogs/回执/余额/nonce）与签名广播（EIP-155）。
/// 实现一：AnkrBscChainGateway（JSON-RPC over HttpClient）；按地址兜底走 Etherscan V2。
/// 测试用假实现驱动扫链/归集状态机——链上全链路手工验证不进 CI（计划第 8 节）。
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

    /// <summary>按地址分批兜底（缩到 1 块仍读不完时）；分页拉取，Incomplete=true 表示还有下一页。</summary>
    Task<AddressTransferPage> GetTransfersForAddressAsync(
        string contractAddress, string address, long fromBlock, long toBlock,
        int page, int pageSize, CancellationToken ct = default);

    /// <summary>balanceOf，返回原始整数十进制字符串。</summary>
    Task<string> GetTokenBalanceRawAsync(string contractAddress, string address, CancellationToken ct = default);

    /// <summary>BNB 余额（ether 十进制）。</summary>
    Task<decimal> GetBnbBalanceAsync(string address, CancellationToken ct = default);

    Task<long> GetTransactionCountAsync(string address, CancellationToken ct = default);

    /// <summary>回执；无回执返回 BlockNumber/Status 为 null 的记录。</summary>
    Task<TransactionReceipt> GetTransactionReceiptAsync(string txHash, CancellationToken ct = default);

    /// <summary>
    /// 签名并广播一笔 EIP-155 legacy 交易（nonce 显式传入：重签替换必须复用原 nonce，
    /// 由调用方持久化在归集任务上）。dataHex 空串 = 普通 BNB 转账。返回交易哈希。
    /// </summary>
    Task<string> SendTransactionAsync(
        string privateKey, string to, string dataHex, string valueWei, long chainId, long nonce,
        CancellationToken ct = default);

    Task<decimal> GetGasPriceGweiAsync(CancellationToken ct = default);
}
