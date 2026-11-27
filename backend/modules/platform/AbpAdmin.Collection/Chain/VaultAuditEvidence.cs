using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Ledger;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AbpAdmin.Collection.Chain;

/// <summary>把同一 PostgreSQL 快照中的负债和归集批次，与同一个链上余额参考块对齐。</summary>
internal sealed class VaultAuditEvidence
{
    internal const decimal StorageLimit = 10_000_000_000m;
    private sealed record Sweep(Guid Id, string Address, Guid UserId, decimal Gross, bool Posted, long? Block,
        string? Hash, string[]? PreviousHashes);
    private sealed record Deposit(Guid TaskId, Guid UserId, string Address, decimal? Gross, decimal? Net, decimal? Fee,
        bool Posted, decimal? Credit, bool CorrectOwner);
    private sealed record Payouts(bool Uncertain, long? LastNonce);
    private readonly Payouts? _payouts;
    private readonly List<Sweep> _sweeps;
    private readonly ILookup<Guid, Deposit> _deposits;
    internal decimal UserTotal { get; }

    private VaultAuditEvidence(decimal userTotal, List<Sweep> sweeps, List<Deposit> deposits, Payouts? payouts)
        => (UserTotal, _sweeps, _deposits, _payouts) = (userTotal, sweeps, deposits.ToLookup(d => d.TaskId), payouts);

    /// <summary>调用方持有短 REPEATABLE READ 事务。归集状态与净额流水同事务提交，避免跨查询重计。</summary>
    internal static async Task<VaultAuditEvidence> ReadAsync(CollectionDbContext db, ColNetwork network, ColToken token,
        CollectionCurrency currency, long referenceBlock, CancellationToken ct)
    {
        var total = await db.Balances.AsNoTracking().Where(b => b.Currency == currency).GroupBy(b => b.Currency)
            .Select(g => Math.Min(StorageLimit, g.Sum(b => b.Balance + b.Frozen))).SingleOrDefaultAsync(ct);
        var vault = token.VaultAddress!.ToLowerInvariant();
        // 审核时负债已减少。同址出款尚未闭合、或在余额参考块之后才发生时，差额可能掩盖缺口。
        var payouts = await db.WithdrawOrders.AsNoTracking().Where(o => o.NetworkName == network.Name && o.Currency == currency
                && (o.Status == WithdrawStatus.Processing || o.Status == WithdrawStatus.Broadcasting
                    || o.Status == WithdrawStatus.BroadcastFailed || o.Status == WithdrawStatus.Completed)
                && (o.BroadcastSenderAddress == null
                    ? db.Networks.Any(n => n.Id == network.Id && n.WithdrawVaultAddress != null && n.WithdrawVaultAddress.ToLower() == vault)
                    : o.BroadcastSenderAddress.ToLower() == vault))
            .GroupBy(o => o.Currency)
            .Select(g => new Payouts(g.Any(o => o.Status != WithdrawStatus.Completed || o.BroadcastSenderAddress == null
                || o.TxHash == null || o.NonceUsed == null || o.NonceUsed < 0), g.Max(o => o.NonceUsed))).SingleOrDefaultAsync(ct);
        var sweeps = await db.SweepTasks.AsNoTracking().Where(t => t.NetworkId == network.Id && t.TokenId == token.Id
                && (t.BroadcastVaultAddress == null || t.BroadcastVaultAddress.ToLower() == vault)
                && (t.Status == SweepTaskStatus.Swept
                    ? t.TxBlockNumber == null || t.TxBlockNumber > referenceBlock
                    : t.TxHash != null || t.BroadcastAt != null || t.TxBlockNumber != null || t.NonceUsed != null))
            .Select(t => new Sweep(t.Id, t.Address, t.UserId, t.Amount, t.Status == SweepTaskStatus.Swept,
                t.TxBlockNumber, t.TxHash, t.PreviousTxHashes)).ToListAsync(ct);
        var ids = sweeps.Select(t => t.Id).ToArray();
        if (ids.Length == 0) return new(total, sweeps, [], payouts);
        var deposits = await (from d in db.DepositOrders.AsNoTracking()
            where d.SweepTaskId != null && ids.Contains(d.SweepTaskId.Value) && d.NetworkId == network.Id && d.TokenId == token.Id
                && (d.Status == DepositOrderStatus.Sweeping || d.Status == DepositOrderStatus.Swept)
            join credit in db.BalanceTxns.AsNoTracking() on d.Id.ToString() equals credit.IdempotencyKey into credits
            from credit in credits.DefaultIfEmpty()
            select new Deposit(d.SweepTaskId!.Value, d.UserId, d.ToAddress, d.Amount, d.ActualAmount, d.Fee,
                d.Status == DepositOrderStatus.Swept, credit == null ? null : credit.Amount,
                credit != null && credit.UserId == d.UserId && credit.Currency == currency
                    && credit.Action == BalanceActions.DepositCredit)).ToListAsync(ct);
        return new(total, sweeps, deposits, payouts);
    }

    /// <summary>只接受金额、合约、哈希、块高和收发地址均一致的成功回执。替换交易按同一批次只算一次。</summary>
    internal async Task<(decimal? Pending, decimal? After)> CompareAsync(IBscChainGateway gateway, ColToken token,
        long referenceBlock, int confirmations, ILogger logger, CancellationToken ct)
    {
        if (_payouts != null)
        {
            if (_payouts.Uncertain) return (null, null);
            try
            {
                // 参考块之前及该块内已消耗的 nonce 均小于账户交易数。
                var nonce = await gateway.GetTransactionCountAtBlockAsync(token.VaultAddress!, referenceBlock, ct);
                if (nonce == null || !(_payouts.LastNonce < nonce)) return (null, null);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "金库核对无法验证共址提现的出款区块");
                return (null, null);
            }
        }
        decimal pending = 0, after = 0;
        var counted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long? latestPostedBlock = null;
        foreach (var sweep in _sweeps)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(sweep.Hash)) return (null, null);
                long? includedBlock = null;
                foreach (var hash in (sweep.PreviousHashes ?? []).Prepend(sweep.Hash).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    var receipt = await gateway.GetTransactionReceiptAsync(hash, ct);
                    if (!string.Equals(receipt.TxHash, hash, StringComparison.OrdinalIgnoreCase)) return (null, null);
                    if (!sweep.Posted && receipt.Status == null && receipt.BlockNumber == null) continue;
                    if (receipt.BlockNumber is not (>= 0 and var block) || receipt.Status is not (0 or 1)) return (null, null);
                    if (sweep.Posted && (receipt.Status != 1 || (sweep.Block.HasValue && block != sweep.Block))) return (null, null);
                    if (receipt.Status == 0 || (!sweep.Posted && block > referenceBlock)) continue;

                    var (logs, complete) = await gateway.GetReceiptTransferLogsAsync(hash, token.ContractAddress, block, ct);
                    if (!complete || !logs.Any(l => l.BlockNumber == block
                        && string.Equals(l.TxHash, hash, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(l.From, sweep.Address, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(l.To, token.VaultAddress, StringComparison.OrdinalIgnoreCase)
                        && DepositScanManager.TryScaleRaw(l.RawAmount, token.Decimals, out _) == sweep.Gross))
                        return (null, null);
                    if (includedBlock.HasValue || !counted.Add(hash)) return (null, null);
                    includedBlock = block;
                }
                if (!includedBlock.HasValue && !sweep.Posted)
                {
                    if (sweep.Block <= referenceBlock) return (null, null);
                    continue;
                }
                if (sweep.Posted && includedBlock <= referenceBlock) continue;
                if (!includedBlock.HasValue) return (null, null);
                var orders = _deposits[sweep.Id].ToArray();
                if (orders.Length == 0 || orders.Any(d => d.Gross is not > 0 || d.Posted != sweep.Posted
                    || d.UserId != sweep.UserId || !string.Equals(d.Address, sweep.Address, StringComparison.OrdinalIgnoreCase))
                    || orders.Sum(d => d.Gross!.Value) != sweep.Gross)
                    return (null, null);
                foreach (var d in orders)
                {
                    var net = CollectionLedgerPoster.ResolveCreditAmount(d.Gross, d.Net, d.Fee);
                    if (net <= 0 || net > d.Gross || (sweep.Posted
                        ? !d.CorrectOwner || d.Credit != net : d.Credit != null)) return (null, null);
                    if (sweep.Posted) after += net;
                    else pending += net;
                }
                if (pending >= StorageLimit || after >= StorageLimit) return (null, null);
                if (sweep.Posted) latestPostedBlock = Math.Max(latestPostedBlock ?? 0, includedBlock.Value);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "金库核对无法验证归集 {SweepId} 的回执或净额", sweep.Id);
                return (null, null);
            }
        }
        if (latestPostedBlock.HasValue)
        {
            try
            {
                var finalHead = await gateway.GetLatestBlockNumberAsync(ct);
                if (finalHead < confirmations || finalHead - confirmations < latestPostedBlock.Value)
                    return (null, null);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "金库核对无法验证参考块后入账交易的最终确认深度");
                return (null, null);
            }
        }
        return (pending, after);
    }
}
