using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

/// <summary>
/// 会员资金查询 + 资金看板（管理端只读，收款查看权限 Collections.Default——与菜单/路由同位）：
/// member-balance-accounts 分页列出已有余额行的用户（一人一行，按最近更新倒序）；
/// member-\* 按 userId 查该用户余额与流水，查询内核与用户侧 my-\* 单源共享（<see cref="BalanceQueries"/>，
/// 含充值行链上凭证回填口径）；用户名解析在前端走 identity 搜索（模块不做身份耦合——
/// 用户不存在时余额返回 0 行不报错，与「新用户无余额行」同形，因此不会出现在账户列表里）。
/// dashboard 是纯 DB 聚合的看板（今日 = UTC 日界，与当日笔数限额同口径），不碰链——
/// RPC 不进看板关键路径（热钱包余额/熔断在热钱包监控页）。
/// AssertHost 双保险（同模块其余 Admin 服务纪律）。
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

    /// <summary>
    /// 已有余额行的用户（一人一行）。没有发生过资金往来的用户没有余额行，不出现在这里。
    /// 缺一种币时该币可用/冻结为 0。用户名不在此解析。
    /// </summary>
    public virtual async Task<PagedResultDto<MemberBalanceAccountDto>> GetMemberBalanceAccountsAsync(
        int skipCount, int maxResultCount)
    {
        AssertHost();
        if (maxResultCount <= 0 || maxResultCount > 200)
        {
            maxResultCount = 20;
        }

        if (skipCount < 0)
        {
            skipCount = 0;
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var grouped = dbContext.Balances.AsNoTracking()
            .GroupBy(b => b.UserId)
            .Select(g => new { UserId = g.Key, UpdatedAt = g.Max(b => b.UpdatedAt) });
        var total = await grouped.CountAsync();
        var page = await grouped
            .OrderByDescending(x => x.UpdatedAt)
            .ThenBy(x => x.UserId)
            .Skip(skipCount)
            .Take(maxResultCount)
            .ToListAsync();
        var userIds = page.Select(x => x.UserId).ToList();
        var rows = userIds.Count == 0
            ? new List<ColBalance>()
            : await dbContext.Balances.AsNoTracking()
                .Where(b => userIds.Contains(b.UserId))
                .ToListAsync();
        var items = page.Select(entry =>
        {
            var mine = rows.Where(b => b.UserId == entry.UserId).ToList();
            return new MemberBalanceAccountDto
            {
                UserId = entry.UserId,
                UpdatedAt = entry.UpdatedAt,
                UsdtBalance = CollectionAmounts.Format(Sum(mine, CollectionCurrency.Usdt, b => b.Balance)),
                UsdtFrozen = CollectionAmounts.Format(Sum(mine, CollectionCurrency.Usdt, b => b.Frozen)),
                UsdcBalance = CollectionAmounts.Format(Sum(mine, CollectionCurrency.Usdc, b => b.Balance)),
                UsdcFrozen = CollectionAmounts.Format(Sum(mine, CollectionCurrency.Usdc, b => b.Frozen)),
            };
        }).ToList();
        await uow.CompleteAsync();
        return new PagedResultDto<MemberBalanceAccountDto>(total, items);
    }

    private static decimal Sum(List<ColBalance> rows, CollectionCurrency currency, Func<ColBalance, decimal> selector)
        => rows.Where(b => b.Currency == currency).Sum(selector);

    /// <summary>某用户的两种稳定币余额（没有余额行时返回 0，不缺项）。</summary>
    public virtual async Task<ListResultDto<MyBalanceDto>> GetMemberBalancesAsync(Guid userId)
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var items = await BalanceQueries.GetBalancesAsync(dbContext, userId);
        await uow.CompleteAsync();
        return new ListResultDto<MyBalanceDto>(items);
    }

    /// <summary>某用户的余额流水（倒序分页，可选时间范围；充值行回填链上凭证）。</summary>
    public virtual async Task<PagedResultDto<BalanceTxnDto>> GetMemberTxnsAsync(
        Guid userId, int skipCount, int maxResultCount,
        DateTime? fromTime = null, DateTime? toTime = null, Guid? txnId = null, Guid? withdrawalId = null)
    {
        AssertHost();
        if (maxResultCount <= 0 || maxResultCount > 200)
        {
            maxResultCount = 20;
        }

        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var (total, items) = await BalanceQueries.GetTxnsAsync(
            dbContext, userId, skipCount, maxResultCount, fromTime, toTime, txnId, withdrawalId);
        await uow.CompleteAsync();
        return new PagedResultDto<BalanceTxnDto>(total, items);
    }

    /// <summary>
    /// 资金看板聚合（对齐线上充提看板核心口径）：充值/提现各状态计数与金额、今日/累计、
    /// 双侧手续费、在途提现敞口、用户余额合计（平台负债）、金库核对最新留痕、大额门持有。
    /// 全部只读 DB 聚合（无 RPC），金额十进制字符串。今日 = UTC 日界
    ///（全站 Clock.Kind=Utc，「今日」按 UTC 0 点切，与 MaxDailyCount 的 UTC 日口径一致）。
    /// </summary>
    public virtual async Task<TreasuryDashboardDto> GetDashboardAsync()
    {
        AssertHost();
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var todayStart = Clock.Now.Date;

        var deposits = await dbContext.DepositOrders.AsNoTracking()
            .GroupBy(o => o.Status)
            .Select(g => new StatusBucketDto
            {
                Status = g.Key.ToString(),
                Count = g.Count(),
                Total = CollectionAmounts.Format(g.Sum(o => o.Amount ?? 0m)),
            })
            .ToListAsync();
        var withdrawRows = await dbContext.WithdrawOrders.AsNoTracking()
            .GroupBy(o => new { o.Status, o.Currency })
            .Select(g => new
            {
                g.Key.Status,
                g.Key.Currency,
                Count = g.Count(),
                Total = g.Sum(o => o.Amount),
            })
            .ToListAsync();
        var withdraws = withdrawRows.Select(g => new StatusBucketDto
        {
            Status = g.Status.ToString(),
            Currency = g.Currency.ToString(),
            Count = g.Count,
            Total = CollectionAmounts.Format(g.Total),
        }).ToList();

        var depositToday = await dbContext.DepositOrders.AsNoTracking()
            .Where(o => o.CreationTime >= todayStart)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(o => o.Amount ?? 0m) })
            .FirstOrDefaultAsync();
        var withdrawTodayRows = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.CreationTime >= todayStart)
            .GroupBy(o => o.Currency)
            .Select(g => new { g.Key, Count = g.Count(), Total = g.Sum(o => o.Amount) })
            .ToListAsync();
        var withdrawFeeRows = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.Status == WithdrawStatus.Completed)
            .GroupBy(o => o.Currency)
            .Select(g => new { g.Key, Total = g.Sum(o => o.Fee) })
            .ToListAsync();
        var inFlightRows = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.Status == WithdrawStatus.Processing
                        || o.Status == WithdrawStatus.Broadcasting
                        || o.Status == WithdrawStatus.BroadcastFailed)
            .GroupBy(o => o.Currency)
            .Select(g => new { g.Key, Total = g.Sum(o => o.ActualAmount) })
            .ToListAsync();
        var depositFee = await dbContext.DepositOrders.AsNoTracking()
            .Where(o => o.Status == DepositOrderStatus.Swept)
            .SumAsync(o => (decimal?)o.Fee);

        // 聚合在 DB 侧、枚举转字符串在客户端——投影里的 g.Key.ToString() 会被 EF 翻译成
        // int::text（枚举列存 int），负债卡会显示「用户 0 合计」；
        // 状态桶能出枚举名是另一条求值路径，统一收客户端转换防同坑
        var balanceRows = await dbContext.Balances.AsNoTracking()
            .GroupBy(b => b.Currency)
            .Select(g => new { Currency = g.Key, Balance = g.Sum(b => b.Balance), Frozen = g.Sum(b => b.Frozen) })
            .ToListAsync();
        var userBalances = balanceRows.Select(g => new UserBalanceBucketDto
        {
            Currency = g.Currency.ToString(),
            Balance = CollectionAmounts.Format(g.Balance),
            Frozen = CollectionAmounts.Format(g.Frozen),
        }).ToList();

        // 金库最新留痕按币取最近一条（DB 侧组内取最新，不整表物化——留痕表只增不减）
        var vaultChecks = await dbContext.VaultChecks.AsNoTracking()
            .GroupBy(v => v.Symbol)
            .Select(g => new VaultCheckLatestDto
            {
                Symbol = g.Key,
                VaultBalance = CollectionAmounts.Format(
                    g.OrderByDescending(x => x.CheckedAt).First().VaultBalance),
                UserTotal = CollectionAmounts.Format(
                    g.OrderByDescending(x => x.CheckedAt).First().UserTotal),
                Difference = CollectionAmounts.Format(
                    g.OrderByDescending(x => x.CheckedAt).First().Difference),
                CheckedAt = g.OrderByDescending(x => x.CheckedAt).First().CheckedAt,
            })
            .ToListAsync();

        var held = await dbContext.DepositOrders.AsNoTracking()
            .Where(o => o.Status == DepositOrderStatus.Confirmed && o.SweepTaskId == null && !o.ManuallyReleased)
            .GroupBy(_ => 1)
            .Select(g => new { Count = g.Count(), Total = g.Sum(o => o.Amount ?? 0m) })
            .FirstOrDefaultAsync();
        await uow.CompleteAsync();

        return new TreasuryDashboardDto
        {
            GeneratedAt = Clock.Now,
            DepositByStatus = deposits,
            WithdrawByStatus = withdraws,
            HeldDepositCount = held?.Count ?? 0,
            HeldDepositTotal = CollectionAmounts.Format(held?.Total ?? 0m),
            DepositTodayCount = depositToday?.Count ?? 0,
            DepositTodayAmount = CollectionAmounts.Format(depositToday?.Total ?? 0m),
            WithdrawTodayCount = withdrawTodayRows.Sum(row => row.Count),
            WithdrawTodayAmount = SingleCurrencyAmount(withdrawTodayRows.Select(row => (row.Key.ToString(), row.Total))),
            DepositFeeTotal = CollectionAmounts.Format(depositFee ?? 0m),
            WithdrawFeeTotal = SingleCurrencyAmount(withdrawFeeRows.Select(row => (row.Key.ToString(), row.Total))),
            WithdrawFees = withdrawFeeRows.Select(row => new CurrencyAmountDto
            {
                Currency = row.Key.ToString(),
                Amount = CollectionAmounts.Format(row.Total),
            }).ToList(),
            WithdrawInFlightAmount = SingleCurrencyAmount(inFlightRows.Select(row => (row.Key.ToString(), row.Total))),
            WithdrawInFlight = inFlightRows.Select(row => new CurrencyAmountDto
            {
                Currency = row.Key.ToString(),
                Amount = CollectionAmounts.Format(row.Total),
            }).ToList(),
            UserBalances = userBalances,
            VaultChecks = vaultChecks,
        };
    }

    private void AssertHost()
    {
        // 表没有租户列：租户身份调用直接拒绝（计划第 2 节）
        if (CurrentTenant.Id != null)
        {
            throw new BusinessException(CollectionErrorCodes.HostOnly);
        }
    }

    private static string SingleCurrencyAmount(IEnumerable<(string Currency, decimal Amount)> rows)
    {
        var list = rows.ToList();
        if (list.Count > 1)
        {
            return "0";
        }

        return CollectionAmounts.Format(list.Count == 0 ? 0m : list[0].Amount);
    }
}

/// <summary>一个用户的 USDT / USDC 余额账户。缺币种时对应金额为 0。</summary>
public class MemberBalanceAccountDto
{
    public Guid UserId { get; set; }

    public string UsdtBalance { get; set; } = "0";

    public string UsdtFrozen { get; set; } = "0";

    public string UsdcBalance { get; set; } = "0";

    public string UsdcFrozen { get; set; } = "0";

    public DateTime UpdatedAt { get; set; }
}

/// <summary>资金看板（纯 DB 聚合，不碰链——RPC 不进看板关键路径；热钱包余额/熔断在热钱包监控页）。</summary>
public class TreasuryDashboardDto
{
    public DateTime GeneratedAt { get; set; }

    /// <summary>进款单按状态：计数 + 毛额合计（超精度单毛额按 0 计入合计）。</summary>
    public List<StatusBucketDto> DepositByStatus { get; set; } = new();

    /// <summary>提现单按状态：计数 + 总额合计。</summary>
    public List<StatusBucketDto> WithdrawByStatus { get; set; } = new();

    /// <summary>大额门持有（Confirmed 且未挂任务，等放行敞口）。</summary>
    public int HeldDepositCount { get; set; }

    public string HeldDepositTotal { get; set; } = default!;

    /// <summary>今日进款（UTC 日界）：单数 + 毛额。</summary>
    public int DepositTodayCount { get; set; }

    public string DepositTodayAmount { get; set; } = default!;

    /// <summary>今日提现（UTC 日界）：单数 + 总额。</summary>
    public int WithdrawTodayCount { get; set; }

    public string WithdrawTodayAmount { get; set; } = default!;

    /// <summary>充值手续费累计（已入账 Swept 单的 Fee 合计）。</summary>
    public string DepositFeeTotal { get; set; } = default!;

    /// <summary>一种币时的提现手续费。多种币时为 0，看 WithdrawFees。</summary>
    public string WithdrawFeeTotal { get; set; } = default!;

    /// <summary>提现手续费，按币。多于一种币时上面的合计字符串为 0，以本列表为准。</summary>
    public List<CurrencyAmountDto> WithdrawFees { get; set; } = new();

    /// <summary>审核后未清偿的提现净额。一种币时等于合计字符串；待审不在这里，广播失败在这里。</summary>
    public string WithdrawInFlightAmount { get; set; } = default!;

    public List<CurrencyAmountDto> WithdrawInFlight { get; set; } = new();

    /// <summary>用户余额合计（按币：可用/冻结——平台负债口径）。</summary>
    public List<UserBalanceBucketDto> UserBalances { get; set; } = new();

    /// <summary>各币最新金库核对留痕。</summary>
    public List<VaultCheckLatestDto> VaultChecks { get; set; } = new();
}

/// <summary>状态桶（计数 + 金额合计，金额十进制字符串）。</summary>
public class StatusBucketDto
{
    public string Status { get; set; } = default!;

    /// <summary>提现按币拆开。进款单没有币种列时为空。</summary>
    public string Currency { get; set; } = "";

    public int Count { get; set; }

    public string Total { get; set; } = default!;
}

public class CurrencyAmountDto
{
    public string Currency { get; set; } = default!;

    public string Amount { get; set; } = default!;
}

/// <summary>按币用户余额桶。</summary>
public class UserBalanceBucketDto
{
    public string Currency { get; set; } = default!;
    public string Balance { get; set; } = default!;
    public string Frozen { get; set; } = default!;
}

/// <summary>最新一次金库核对。</summary>
public class VaultCheckLatestDto
{
    public string Symbol { get; set; } = default!;
    public string VaultBalance { get; set; } = default!;
    public string UserTotal { get; set; } = default!;

    /// <summary>VaultBalance - UserTotal；负 = 金库 &lt; 用户余额（缺口）。</summary>
    public string Difference { get; set; } = default!;
    public DateTime CheckedAt { get; set; }
}
