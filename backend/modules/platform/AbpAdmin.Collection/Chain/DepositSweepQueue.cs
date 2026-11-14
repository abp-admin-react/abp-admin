using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;

namespace AbpAdmin.Collection.Chain;

internal static class DepositSweepQueue
{
    /// <summary>串行化同充值地址的挂队和激活。无事务时 PostgreSQL 会立即释放锁。</summary>
    internal static Task LockAddressAsync(CollectionDbContext db, Guid addressId, CancellationToken ct = default)
    {
        if (db.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException("Deposit sweep queue requires an active transaction.");
        }
        var key = addressId.ToString("D");
        return db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtext('Collection:DepositSweepQueue'), hashtext({key}))", ct);
    }

    /// <summary>调用者持有该地址锁，将新任务和同地址同币种的进款关联一起提交。</summary>
    internal static async Task EnqueueAsync(
        CollectionDbContext db, IReadOnlyList<ColDepositOrder> orders, Func<Guid> newId,
        DateTime now, CancellationToken ct = default)
    {
        if (orders.Count == 0)
        {
            return;
        }

        var first = orders[0];
        var task = await db.SweepTasks.AsNoTracking()
            .Where(t => t.AddressId == first.AddressId && t.TokenId == first.TokenId
                        && t.Status == SweepTaskStatus.Queued && t.Amount == 0
                        && t.TxHash == null && t.GasTxHash == null && t.GasNonceUsed == null
                        && t.NonceUsed == null && t.BroadcastAt == null)
            .OrderBy(t => t.CreationTime).ThenBy(t => t.Id)
            .FirstOrDefaultAsync(ct);

        if (task == null)
        {
            var address = await db.DepositAddresses.AsNoTracking().SingleAsync(a => a.Id == first.AddressId, ct);
            task = new ColSweepTask(newId())
            {
                NetworkId = first.NetworkId,
                TokenId = first.TokenId,
                UserId = first.UserId,
                AddressId = address.Id,
                Address = address.Address,
                AddressIndex = address.AddressIndex,
                Status = SweepTaskStatus.Queued,
                StatusChangedAt = now,
                CreationTime = now,
            };
            db.SweepTasks.Add(task);
        }

        foreach (var order in orders)
        {
            order.SweepTaskId = task.Id;
        }
    }
}
