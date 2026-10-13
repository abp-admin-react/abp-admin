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

/// <summary>我的余额（USDT / USDC）。</summary>
public class MyBalanceDto
{
    public string Currency { get; set; } = default!;

    /// <summary>十进制字符串（边界一律字符串，计划第 2 节）。</summary>
    public string Balance { get; set; } = default!;
}

/// <summary>我的余额流水（分页、倒序）。</summary>
public class BalanceTxnDto
{
    public Guid Id { get; set; }

    public string Action { get; set; } = default!;

    public string Currency { get; set; } = default!;

    /// <summary>带符号十进制字符串：充值/回冲为正，扣款为负。</summary>
    public string Amount { get; set; } = default!;

    public string BalanceAfter { get; set; } = default!;

    public DateTime CreationTime { get; set; }

    /// <summary>业务类型（TopUp / Order …）；无归因为 null。</summary>
    public string? BusinessType { get; set; }

    /// <summary>业务单 Id（充值行 = 流水自身 Id；扣款行 = 订单 Id）。对账查询原值用；
    /// 前端的「关联订单」展示走流水 Id 反查端点（order-refs-by-balance-txn），不经此字段。</summary>
    public Guid? BusinessId { get; set; }

    /// <summary>回冲行的原扣款流水 Id（用户侧把回冲关联回原订单用）。</summary>
    public Guid? RefTxnId { get; set; }

    /// <summary>充值行的链上转账凭证（ColDepositOrders.TxHash 回填；非充值行为 null）。</summary>
    public string? TxHash { get; set; }
}

/// <summary>
/// 用户侧余额接口（计划第 3.1 节「用户和管理端看余额、流水，走收款模块自己的应用服务，
/// 不放进集成服务」）。只查 CurrentUser.Id；这两套表没有租户列，租户身份直接拒绝（Host 守卫）。
/// 订单页面展示两种余额就调它（不引用收款实现项目）。
/// </summary>
[Authorize]
public class CollectionBalanceAppService : ApplicationService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;

    public CollectionBalanceAppService(
        IDbContextProvider<CollectionDbContext> dbContextProvider)
    {
        _dbContextProvider = dbContextProvider;
    }

    /// <summary>我的两种稳定币余额（没有余额行时返回 0，不缺项）。</summary>
    public virtual async Task<ListResultDto<MyBalanceDto>> GetMyBalancesAsync()
    {
        AssertHost();
        var userId = CurrentUser.Id!.Value;

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var rows = await dbContext.Balances.AsNoTracking()
            .Where(b => b.UserId == userId)
            .ToDictionaryAsync(b => b.Currency, b => b.Balance);

        var result = new List<MyBalanceDto>();
        foreach (var currency in new[] { Contracts.CollectionCurrency.Usdt, Contracts.CollectionCurrency.Usdc })
        {
            result.Add(new MyBalanceDto
            {
                Currency = currency.ToString(),
                Balance = Contracts.CollectionAmounts.Format(rows.TryGetValue(currency, out var v) ? v : 0m),
            });
        }

        return new ListResultDto<MyBalanceDto>(result);
    }

    /// <summary>
    /// 我的余额流水（只看自己的，倒序分页，可选时间范围）。
    /// 充值行批量回填链上凭证：贷记的幂等键 = 进款单 Id（CreditCoreAsync 的写入口径），
    /// 据此查 ColDepositOrders.TxHash——用户对账要能看到链上转账，不只是余额数字。
    /// </summary>
    public virtual async Task<PagedResultDto<BalanceTxnDto>> GetMyTxnsAsync(
        int skipCount, int maxResultCount, DateTime? fromTime = null, DateTime? toTime = null)
    {
        AssertHost();
        var userId = CurrentUser.Id!.Value;
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
                Amount = Contracts.CollectionAmounts.Format(t.Amount),
                BalanceAfter = Contracts.CollectionAmounts.Format(t.BalanceAfter),
                t.CreationTime,
                t.BusinessType,
                t.BusinessId,
                t.RefTxnId,
                t.IdempotencyKey,
            })
            .ToListAsync();

        // 充值行回填链上凭证（批量一次查）：贷记的幂等键 = 进款单 Id
        //（读写口径唯一定义处 BalanceIdempotencyKeys——写入在 SweepManager 确认入账），
        // 据此查 ColDepositOrders.TxHash——用户对账要能看到链上转账，不只是余额数字
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
            throw new BusinessException(Contracts.CollectionErrorCodes.HostOnly);
        }
    }
}
