using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Collection.Services;

/// <summary>
/// 会员资金查询（管理端，只读）：按用户查余额与流水——对齐线上 memberFunds/memberFundFlowPage。
/// 查询语义与 <see cref="CollectionBalanceAppService"/> 的 my-* 完全同款（含充值行链上凭证回填），
/// 仅把 CurrentUser 换成参数。用户名解析在前端走 identity 搜索（模块不做身份耦合——
/// 用户不存在时余额返回 0 行不报错，与「新用户无余额行」同形）。
/// 权限：收款查看（Collections.Default）+ AssertHost 双保险。
/// </summary>
[Authorize(AbpAdmin.Collection.Permissions.CollectionPermissions.Collections.Default)]
public class CollectionBalanceAdminAppService : ApplicationService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;

    public CollectionBalanceAdminAppService(
        IDbContextProvider<CollectionDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    /// <summary>某用户的两种稳定币余额（没有余额行时返回 0，不缺项）。</summary>
    public virtual async Task<ListResultDto<MyBalanceDto>> GetMemberBalancesAsync(Guid userId)
    {
        AssertHost();
        var dbContext = await _dbContextProvider.GetDbContextAsync();
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

        return new ListResultDto<MyBalanceDto>(result);
    }

    /// <summary>某用户的余额流水（倒序分页，可选时间范围；充值行回填链上凭证）。</summary>
    public virtual async Task<PagedResultDto<BalanceTxnDto>> GetMemberTxnsAsync(
        Guid userId, int skipCount, int maxResultCount,
        DateTime? fromTime = null, DateTime? toTime = null)
    {
        AssertHost();
        if (maxResultCount <= 0 || maxResultCount > 200)
        {
            maxResultCount = 20;
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
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

        // 充值行回填链上凭证（与 my-txns 同款：贷记幂等键 = 进款单 Id）
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

        return new PagedResultDto<BalanceTxnDto>(total, items);
    }

    private void AssertHost()
    {
        // 表没有租户列：租户身份调用直接拒绝（计划第 2 节）
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }
}
