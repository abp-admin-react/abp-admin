using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AbpAdmin.Collection.Chain;

/// <summary>每币每轮合并已提交的变更。离开非事务性轮次时读取最终快照，部分成功后中断也可通知。</summary>
internal sealed class ChainRecordChanges(CollectionDbContext db, string symbol, IChainPushNotifier? notifier, ILogger logger)
    : IAsyncDisposable
{
    private readonly HashSet<Guid> _deposits = [];
    private readonly Dictionary<Guid, ColSweepTask?> _sweeps = [];

    public void DepositChanged(Guid id) => _deposits.Add(id);
    public void SweepCreated(Guid id) => _sweeps.TryAdd(id, null);
    public void ObserveSweep(ColSweepTask before) => _sweeps.TryAdd(before.Id, before);

    public async ValueTask DisposeAsync()
    {
        if (notifier == null) return;
        try
        {
            if (_deposits.Count > 0)
            {
                var rows = await db.DepositOrders.AsNoTracking().Where(o => _deposits.Contains(o.Id)).ToListAsync();
                foreach (var row in rows)
                {
                    await SendAsync(() => notifier.PushDepositChangedAsync(new ChainDepositChangedEto
                    {
                        DepositOrderId = row.Id, TxHash = row.TxHash, Symbol = symbol, UserId = row.UserId,
                        Status = row.Status.ToString(), Amount = row.Amount is decimal amount ? CollectionAmounts.Format(amount) : null,
                        BlockNumber = row.BlockNumber, CreationTime = row.CreationTime
                    }));
                }
            }

            if (_sweeps.Count > 0)
            {
                var ids = _sweeps.Keys.ToArray();
                var rows = await db.SweepTasks.AsNoTracking().Where(t => ids.Contains(t.Id)).ToListAsync();
                foreach (var row in rows)
                {
                    var before = _sweeps[row.Id];
                    // LastProcessedAt is worker fairness bookkeeping; idle receipt checks must stay silent.
                    if (before != null && before.Status == row.Status && before.Amount == row.Amount &&
                        before.StatusChangedAt == row.StatusChangedAt && before.TxHash == row.TxHash &&
                        before.GasTxHash == row.GasTxHash && before.NonceUsed == row.NonceUsed &&
                        before.FailReason == row.FailReason && before.Retryable == row.Retryable) continue;
                    await SendAsync(() => notifier.PushSweepChangedAsync(new ChainSweepChangedEto
                    {
                        SweepTaskId = row.Id, Symbol = symbol, UserId = row.UserId, Status = row.Status.ToString(),
                        Amount = CollectionAmounts.Format(row.Amount), StatusChangedAt = row.StatusChangedAt
                    }));
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取已提交的 {Symbol} 变更通知快照失败", symbol);
        }
    }

    private async Task SendAsync(Func<Task> send)
    {
        try { await send(); }
        catch (Exception ex) { logger.LogWarning(ex, "发送 {Symbol} 收款运营变更通知失败", symbol); }
    }
}
