using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Desensitization;
using AbpAdmin.Grids;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Identity;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Services;

/// <summary>宿主端会员余额、流水和资金看板，使用收款查看权限。账户以用户为单位，缺少的币种余额按零计。</summary>
[Authorize(AbpAdmin.Collection.Permissions.CollectionPermissions.Collections.Default)]
public class CollectionBalanceAdminAppService : ApplicationService
{
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly IPermissionChecker _permissions;

    public CollectionBalanceAdminAppService(IDbContextProvider<CollectionDbContext> dbContextProvider,
        IPermissionChecker permissions)
    {
        _dbContextProvider = dbContextProvider;
        _permissions = permissions;
    }

    private sealed class AccountTotals
    {
        public Guid UserId { get; init; }
        public string? UserName { get; init; }
        public string? Email { get; init; }
        public DateTime UpdatedAt { get; init; }
        public decimal UsdtBalance { get; init; }
        public decimal UsdtFrozen { get; init; }
        public decimal UsdcBalance { get; init; }
        public decimal UsdcFrozen { get; init; }
    }

    private static GridQueryDefinition<AccountTotals> AccountGrid(bool canReadDirectory, bool canReadPlainEmail)
    {
        var grid = new GridQueryDefinition<AccountTotals>()
            .SearchableField("userId", row => row.UserId, "eq", "ne")
            .Field("usdtBalance", row => row.UsdtBalance)
            .Field("usdtFrozen", row => row.UsdtFrozen)
            .Field("usdcBalance", row => row.UsdcBalance)
            .Field("usdcFrozen", row => row.UsdcFrozen)
            .Field("updatedAt", row => row.UpdatedAt);
        if (canReadDirectory)
        {
            grid.KeywordOnly(row => row.UserName);
            if (canReadPlainEmail)
                grid.KeywordOnly(row => row.Email);
        }
        return grid;
    }

    /// <summary>原分页入口沿用更新时间倒序和原有的页大小归一规则。</summary>
    public virtual async Task<PagedResultDto<MemberBalanceAccountDto>> GetMemberBalanceAccountsAsync(
        int skipCount, int maxResultCount)
    {
        AssertHost();
        var page = await ReadAccountPageAsync(new GridQueryInput
        {
            SkipCount = Math.Max(0, skipCount),
            MaxResultCount = maxResultCount is > 0 and <= 200 ? maxResultCount : 20
        }, false, default);
        return new PagedResultDto<MemberBalanceAccountDto>(page.TotalCount, page.Items);
    }

    [HttpPost("api/app/collection-balance-admin/member-balance-accounts/query")]
    public virtual async Task<PagedResultDto<MemberBalanceGridRowDto>> QueryMemberBalanceAccountsAsync(
        [FromBody] GridQueryInput input, CancellationToken cancellationToken = default)
    {
        AssertHost();
        return await ReadAccountPageAsync(input, true, cancellationToken);
    }

    private async Task<PagedResultDto<MemberBalanceGridRowDto>> ReadAccountPageAsync(
        GridQueryInput input, bool includeIdentity, CancellationToken cancellationToken)
    {
        using var uow = UnitOfWorkManager.Begin(requiresNew: true, isTransactional: false);
        var db = await _dbContextProvider.GetDbContextAsync();
        var canReadDirectory = includeIdentity && await _permissions.IsGrantedAsync(IdentityPermissions.Users.Default);
        var canReadPlainEmail = canReadDirectory && await _permissions.IsGrantedAsync(IdentityPermissions.Users.Update);
        var grid = AccountGrid(canReadDirectory, canReadPlainEmail);
        var balanceRows = db.Balances.AsNoTracking();
        var source = balanceRows.Select(b => new { Balance = b, UserName = (string?)null, Email = (string?)null });
        if (canReadDirectory)
        {
            // 左连接保留无登录身份的资金账户；软删和租户身份均不提供搜索字段或展示标签。
            var directory = db.Set<AccountDirectoryEntry>().Where(u => u.TenantId == null && !u.IsDeleted);
            source = from balance in balanceRows
                     join user in directory on balance.UserId equals user.Id into users
                     from user in users.DefaultIfEmpty()
                     select new { Balance = balance, UserName = (string?)user.UserName, Email = (string?)user.Email };
        }

        // 每个会员只有一个目录条目。缺少某个币种时，条件求和得到零。
        var accounts = source.GroupBy(row => new { row.Balance.UserId, row.UserName, row.Email }).Select(g => new AccountTotals
        {
            UserId = g.Key.UserId, UserName = g.Key.UserName, Email = g.Key.Email,
            UpdatedAt = g.Max(row => row.Balance.UpdatedAt),
            UsdtBalance = g.Sum(row => row.Balance.Currency == CollectionCurrency.Usdt ? row.Balance.Balance : 0m),
            UsdtFrozen = g.Sum(row => row.Balance.Currency == CollectionCurrency.Usdt ? row.Balance.Frozen : 0m),
            UsdcBalance = g.Sum(row => row.Balance.Currency == CollectionCurrency.Usdc ? row.Balance.Balance : 0m),
            UsdcFrozen = g.Sum(row => row.Balance.Currency == CollectionCurrency.Usdc ? row.Balance.Frozen : 0m)
        });
        var filtered = grid.Filter(accounts, input);
        var total = await filtered.LongCountAsync(cancellationToken);
        var page = await grid.OrderBy(filtered, input, "updatedAt", true, row => row.UserId)
            .Skip(input.SkipCount).Take(input.MaxResultCount).ToListAsync(cancellationToken);
        var items = page.Select(row => new MemberBalanceGridRowDto
        {
            UserId = row.UserId,
            UserName = row.UserName,
            Email = row.Email,
            UpdatedAt = row.UpdatedAt,
            UsdtBalance = CollectionAmounts.Format(row.UsdtBalance),
            UsdtFrozen = CollectionAmounts.Format(row.UsdtFrozen),
            UsdcBalance = CollectionAmounts.Format(row.UsdcBalance),
            UsdcFrozen = CollectionAmounts.Format(row.UsdcFrozen)
        }).ToList();

        await uow.CompleteAsync(cancellationToken);
        return new PagedResultDto<MemberBalanceGridRowDto>(total, items);
    }

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
    public virtual Task<PagedResultDto<BalanceTxnDto>> GetMemberTxnsAsync(
        Guid userId, int skipCount, int maxResultCount,
        DateTime? fromTime = null, DateTime? toTime = null, Guid? txnId = null, Guid? withdrawalId = null) =>
        ReadMemberTxnsPageAsync(null, userId, skipCount, maxResultCount, fromTime, toTime, txnId, withdrawalId);

    [HttpPost("api/app/collection-balance-admin/member-txns/query")]
    public virtual Task<PagedResultDto<BalanceTxnDto>> QueryMemberTxnsAsync([FromBody] GridQueryInput grid, Guid userId, DateTime? fromTime = null, DateTime? toTime = null, Guid? txnId = null, Guid? withdrawalId = null) =>
        ReadMemberTxnsPageAsync(grid, userId, grid.SkipCount, grid.MaxResultCount, fromTime, toTime, txnId, withdrawalId);

    private async Task<PagedResultDto<BalanceTxnDto>> ReadMemberTxnsPageAsync(GridQueryInput? grid,
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
            dbContext, userId, skipCount, maxResultCount, fromTime, toTime, txnId, withdrawalId, grid);
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
        var latestChecks = await dbContext.VaultChecks.AsNoTracking()
            .GroupBy(v => v.Symbol)
            .Select(g => g.OrderByDescending(x => x.CheckedAt).ThenBy(x => x.Id).First())
            .ToListAsync();
        var vaultChecks = latestChecks.Select(v => new VaultCheckLatestDto
        {
            Symbol = v.Symbol,
            VaultBalance = CollectionAmounts.Format(v.VaultBalance),
            UserTotal = CollectionAmounts.Format(v.UserTotal),
            Difference = CollectionAmounts.Format(v.Difference),
            ReconciliationStatus = v.ReconciliationStatus.ToString(),
            ReferenceBlock = v.ReferenceBlock,
            ReferenceVaultAddress = v.ReferenceVaultAddress,
            PendingCredit = CollectionAmounts.FormatNullable(v.PendingCredit),
            PostedAfterBlock = CollectionAmounts.FormatNullable(v.PostedAfterBlock),
            ReconciledDifference = CollectionAmounts.FormatNullable(v.ReconciledDifference),
            CheckedAt = v.CheckedAt,
        }).ToList();

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

/// <summary>有身份目录权限时附带当前页的用户标签，邮箱沿用 IdentityUserDto 的脱敏规则。</summary>
public class MemberBalanceGridRowDto : MemberBalanceAccountDto
{
    public string? UserName { get; set; }

    [Masked(MaskKindEnum.Email, PlaintextPermission = IdentityPermissions.Users.Update)]
    public string? Email { get; set; }
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

    /// <summary>金库与当前负债的原始观测差额，最终缺口看 ReconciliationStatus。</summary>
    public string Difference { get; set; } = default!;
    public string ReconciliationStatus { get; set; } = "Legacy";
    public long? ReferenceBlock { get; set; }
    public string? ReferenceVaultAddress { get; set; }
    public string? PendingCredit { get; set; }
    public string? PostedAfterBlock { get; set; }
    public string? ReconciledDifference { get; set; }
    public DateTime CheckedAt { get; set; }
}
