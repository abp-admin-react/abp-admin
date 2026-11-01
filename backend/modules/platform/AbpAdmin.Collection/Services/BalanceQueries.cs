using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using Microsoft.EntityFrameworkCore;

namespace AbpAdmin.Collection.Services;

/// <summary>
/// 余额查询内核（用户侧 my-\* 与管理端 member-\* 的单一实现）：
/// 两币补零、流水 Seq 倒序分页、充值行链上凭证回填（贷记幂等键 = 进款单 Id）。
/// 这是资金对账路径——回填口径只允许此一处定义，两个应用服务各自保留
/// 授权/AssertHost/userId 解析薄壳后委托（逐行复制会分叉用户侧与管理端对账）。
/// </summary>
internal static class BalanceQueries
{
    public static async Task<List<MyBalanceDto>> GetBalancesAsync(CollectionDbContext dbContext, Guid userId)
    {
        var rows = await dbContext.Balances.AsNoTracking()
            .Where(b => b.UserId == userId)
            .ToDictionaryAsync(b => b.Currency, b => new { b.Balance, b.Frozen });

        var result = new List<MyBalanceDto>();
        foreach (var currency in new[] { CollectionCurrency.Usdt, CollectionCurrency.Usdc })
        {
            var row = rows.TryGetValue(currency, out var v) ? v : new { Balance = 0m, Frozen = 0m };
            result.Add(new MyBalanceDto
            {
                Currency = currency.ToString(),
                Balance = CollectionAmounts.Format(row.Balance),
                Frozen = CollectionAmounts.Format(row.Frozen),
                Total = CollectionAmounts.Format(row.Balance + row.Frozen),
            });
        }

        return result;
    }

    public static async Task<(int Total, List<BalanceTxnDto> Items)> GetTxnsAsync(
        CollectionDbContext dbContext, Guid userId,
        int skipCount, int maxResultCount, DateTime? fromTime = null, DateTime? toTime = null)
    {
        var query = dbContext.BalanceTxns.AsNoTracking()
            .Where(t => t.UserId == userId);
        if (fromTime.HasValue)
        {
            query = query.Where(t => t.CreationTime >= fromTime.Value);
        }
        if (toTime.HasValue)
        {
            query = query.Where(t => t.CreationTime < toTime.Value);
        }

        var total = await query.CountAsync();
        var rows = await query
            .OrderByDescending(t => t.Seq)
            .Skip(Math.Max(0, skipCount))
            .Take(maxResultCount)
            .Select(t => new
            {
                t.Id,
                t.Action,
                Currency = t.Currency.ToString(),
                Amount = CollectionAmounts.Format(t.Amount),
                BalanceAfter = CollectionAmounts.Format(t.BalanceAfter),
                t.CreationTime,
                t.BusinessType,
                t.BusinessId,
                t.RefTxnId,
                t.IdempotencyKey,
            })
            .ToListAsync();

        // 充值行回填链上凭证（批量一次查）：贷记的幂等键 = 进款单 Id
        //（读写口径唯一定义处 BalanceIdempotencyKeys——写入在 SweepManager 确认入账），
        // 据此查 ColDepositOrders.TxHash——对账要能看到链上转账，不只是余额数字
        var txHashByTxnId = new Dictionary<Guid, Guid>();
        foreach (var t in rows.Where(x => x.Action == BalanceActions.DepositCredit))
        {
            if (BalanceIdempotencyKeys.TryParseDepositOrder(t.IdempotencyKey, out var depositOrderId))
            {
                txHashByTxnId[t.Id] = depositOrderId;
            }
        }

        var depositOrderIds = txHashByTxnId.Values.Distinct().ToList();
        var txHashByDepositOrderId = depositOrderIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await dbContext.DepositOrders.AsNoTracking()
                .Where(o => depositOrderIds.Contains(o.Id))
                .Select(o => new { o.Id, o.TxHash })
                .ToDictionaryAsync(x => x.Id, x => x.TxHash);

        var items = rows.Select(t => new BalanceTxnDto
        {
            Id = t.Id,
            Action = t.Action,
            Currency = t.Currency,
            Amount = t.Amount,
            BalanceAfter = t.BalanceAfter,
            CreationTime = t.CreationTime,
            BusinessType = t.BusinessType,
            BusinessId = t.BusinessId,
            RefTxnId = t.RefTxnId,
            TxHash = t.Action == BalanceActions.DepositCredit
                     && txHashByTxnId.TryGetValue(t.Id, out var depositOrderId)
                     && txHashByDepositOrderId.TryGetValue(depositOrderId, out var txHash)
                ? txHash
                : null,
        }).ToList();

        return (total, items);
    }
}
