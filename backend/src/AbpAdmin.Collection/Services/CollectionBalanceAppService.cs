using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
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

    /// <summary>我的余额流水（只看自己的，倒序分页）。</summary>
    public virtual async Task<PagedResultDto<BalanceTxnDto>> GetMyTxnsAsync(int skipCount, int maxResultCount)
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

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.Seq)
            .Skip(Math.Max(0, skipCount))
            .Take(maxResultCount)
            .Select(t => new BalanceTxnDto
            {
                Id = t.Id,
                Action = t.Action,
                Currency = t.Currency.ToString(),
                Amount = Contracts.CollectionAmounts.Format(t.Amount),
                BalanceAfter = Contracts.CollectionAmounts.Format(t.BalanceAfter),
                CreationTime = t.CreationTime,
            })
            .ToListAsync();

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
