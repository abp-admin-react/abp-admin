using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbpAdmin.Collection.Chain;

/// <summary>
/// EVM 账户槽位协调器：按 (NetworkId, SenderAddress) 串行化 Nonce 分配、
/// 同槽位候选查询与胜出结算，供打油、归集、提现与管理员清理共用。
/// </summary>
internal static class ChainTxSlotCoordinator
{
    /// <summary>按 (NetworkId, SenderAddress) 获取 PostgreSQL 事务级咨询锁。</summary>
    internal static Task LockAccountSlotAsync(
        CollectionDbContext db, Guid networkId, string senderAddress, CancellationToken ct)
    {
        var key = $"{networkId:N}:{senderAddress.ToLowerInvariant()}";
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('Collection:AccountSlot'), hashtext({key}))", ct);
    }

    /// <summary>
    /// 在账户槽位锁内为串行发送方（如提现热钱包）分配单调递增 Nonce：
    /// 取 max(chainLatest, chainPending, maxOpenAttemptNonce + 1, maxBroadcastingWithdrawNonce + 1)，
    /// 彻底避免负载均衡节点 pending 滞后导致同批多笔交易撞 Nonce。
    /// </summary>
    internal static async Task<long> AllocateSerialNonceAsync(
        CollectionDbContext db,
        Guid networkId,
        string networkName,
        string senderAddress,
        long chainLatestNonce,
        long chainPendingNonce,
        CancellationToken ct)
    {
        var normalized = senderAddress.ToLowerInvariant();
        var maxOpenAttempt = await db.ChainTxAttempts.AsNoTracking()
            .Where(a => a.NetworkId == networkId
                        && a.SenderAddress == normalized
                        && (a.Status == ChainTxAttemptStatus.Signed || a.Status == ChainTxAttemptStatus.Broadcasted))
            .MaxAsync(a => (long?)a.Nonce, ct);

        var maxBroadcastingOrder = await db.WithdrawOrders.AsNoTracking()
            .Where(o => o.NetworkName == networkName
                        && o.BroadcastSenderAddress == normalized
                        && o.Status == WithdrawStatus.Broadcasting
                        && o.NonceUsed != null)
            .MaxAsync(o => o.NonceUsed, ct);

        var dbNext = Math.Max(
            maxOpenAttempt.HasValue ? maxOpenAttempt.Value + 1 : 0L,
            maxBroadcastingOrder.HasValue ? maxBroadcastingOrder.Value + 1 : 0L);

        var chainFloor = Math.Max(chainLatestNonce, chainPendingNonce);
        return Math.Max(chainFloor, dbNext);
    }

    /// <summary>加载同一 (NetworkId, Purpose, RefId, Nonce) 槽位的全部候选签名尝试（按 AttemptNo 倒序）。</summary>
    internal static Task<List<ColChainTxAttempt>> LoadSlotAttemptsAsync(
        CollectionDbContext db,
        Guid networkId,
        ChainTxPurpose purpose,
        Guid refId,
        long nonce,
        CancellationToken ct)
    {
        return db.ChainTxAttempts.AsNoTracking()
            .Where(a => a.NetworkId == networkId
                        && a.Purpose == purpose
                        && a.RefId == refId
                        && a.Nonce == nonce)
            .OrderByDescending(a => a.AttemptNo)
            .ToListAsync(ct);
    }

    /// <summary>
    /// 结算某个账户槽位：将实际出块胜出哈希标为 Confirmed/Reverted，
    /// 并将同槽位其余非 Rejected 候选全部标为 Replaced 并清空原始签名载荷。
    /// </summary>
    internal static async Task MarkSlotSettledAsync(
        CollectionDbContext db,
        Guid networkId,
        ChainTxPurpose purpose,
        Guid refId,
        long? nonce,
        string winningHash,
        ChainTxAttemptStatus winnerStatus,
        CancellationToken ct)
    {
        var normalizedWinner = winningHash.ToLowerInvariant();
        await db.ChainTxAttempts
            .Where(a => a.NetworkId == networkId && a.TxHash == normalizedWinner)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, winnerStatus)
                .SetProperty(a => a.RawSignedTx, (string?)null), ct);

        var siblings = db.ChainTxAttempts.Where(a =>
            a.NetworkId == networkId
            && a.Purpose == purpose
            && a.RefId == refId
            && a.TxHash != normalizedWinner
            && a.Status != ChainTxAttemptStatus.Rejected);
        if (nonce.HasValue)
        {
            var n = nonce.Value;
            siblings = siblings.Where(a => a.Nonce == n);
        }

        await siblings.ExecuteUpdateAsync(s => s
            .SetProperty(a => a.Status, ChainTxAttemptStatus.Replaced)
            .SetProperty(a => a.RawSignedTx, (string?)null), ct);
    }

    /// <summary>
    /// 判断广播异常是否属于节点确定性拒收（如 replacement underpriced / nonce too low / insufficient funds）。
    /// 网络超时、连接断开或 5xx 返回 false，调用方应保留 Signed 状态以便后续全候选回执兜底认领。
    /// </summary>
    internal static bool IsDeterministicTxRejection(Exception ex)
    {
        if (ex is not ChainRpcException cre)
        {
            return false;
        }

        var msg = cre.RpcErrorMessage ?? cre.Message;
        if (string.IsNullOrEmpty(msg))
        {
            return false;
        }

        return msg.Contains("underpriced", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("nonce too low", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("insufficient funds", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("intrinsic gas too low", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("gas too low", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("invalid sender", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("oversized data", StringComparison.OrdinalIgnoreCase)
               || msg.Contains("exceeds block gas limit", StringComparison.OrdinalIgnoreCase);
    }
}
