using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Ledger.Services;

/// <summary>
/// A5 对账：默认排除 IsDemo；includeDemo=true 才纳入演示。需 Finance.Ledger.View（列表）/ Finance.Reconcile.Run（跑对账）。
/// 账本内试算（分录重算 Σ借=Σ贷 + 投影 vs 分录）。
/// 区间按凭证 PostedTime 半开 [rangeStart, rangeEnd)；入参必须带 Z/偏移，否则 ReconcileInvalidRange。
/// Clock.Kind=Utc，ScopeJson 回写亦带 Z。
/// </summary>
[Authorize(FinancePermissions.Ledger.View)]
public class FinReconcileAppService : FinanceAppServiceBase
{
    private readonly IDbContextProvider<LedgerDbContext> _db;
    private readonly Volo.Abp.Uow.IUnitOfWorkManager _unitOfWorkManager;

    public FinReconcileAppService(IDbContextProvider<LedgerDbContext> db, Volo.Abp.Uow.IUnitOfWorkManager unitOfWorkManager)
    {
        _db = db;
        _unitOfWorkManager = unitOfWorkManager;
    }

    public virtual async Task<PagedResultDto<FinReconcileRunDto>> GetListAsync(FinReconcileListInput input)
    {
        var db = await _db.GetDbContextAsync();
        var q = db.ReconcileRuns.AsNoTracking();
        // 列表本身是对账运行记录；includeDemo 筛「该次是否包含演示」
        if (!input.IncludeDemo)
        {
            q = q.Where(r => !r.IncludeDemo);
        }

        if (input.StartTime != null) q = q.Where(r => r.CreationTime >= input.StartTime);
        if (input.EndTime != null) q = q.Where(r => r.CreationTime < input.EndTime);
        var total = await q.CountAsync();
        var (skip, take) = Paging(input);
        var items = await q.OrderByDescending(r => r.CreationTime).Skip(skip).Take(take).ToListAsync();
        return new PagedResultDto<FinReconcileRunDto>(total, items.Select(Map).ToList());
    }

    [Authorize(FinancePermissions.Reconcile.Run)]
    public virtual async Task<FinReconcileRunDto> RunAsync(FinReconcileRunInput input)
    {
        // 三段聚合必须取自同一快照（READ COMMITTED 逐语句快照会让对账期间发生的过账产出假
        // HasDiff 假阳性，进 A1 的 UnreconciledCount）。requiresNew 开独立 UoW 作用域并指定
        // REPEATABLE READ——不能直接挂显式 BeginTransaction：环境事务型 UoW 的连接已在事务里
        // （会抛 InvalidOperationException）。PG 的 RR=快照隔离：纯 SELECT 不阻塞并发过账。
        using var rrUow = _unitOfWorkManager.Begin(
            new Volo.Abp.Uow.AbpUnitOfWorkOptions
            {
                IsTransactional = true,
                IsolationLevel = System.Data.IsolationLevel.RepeatableRead,
            },
            requiresNew: true);
        var db = await _db.GetDbContextAsync();
        var entryQ = from e in db.Entries.AsNoTracking()
                     join v in db.Vouchers.AsNoTracking() on e.VoucherId equals v.Id
                     select new { e, v };
        if (!input.IncludeDemo) entryQ = entryQ.Where(x => !x.v.IsDemo);
        var rangeStart = ToUtc(input.RangeStart, "rangeStart");
        var rangeEnd = ToUtc(input.RangeEnd, "rangeEnd");
        if (rangeStart != null && rangeEnd != null && rangeStart >= rangeEnd)
        {
            throw new BusinessException(FinanceErrorCodes.ReconcileInvalidRange).WithData("Reason", "rangeStart must be earlier than rangeEnd");
        }

        if (rangeStart != null) entryQ = entryQ.Where(x => x.v.PostedTime >= rangeStart);
        if (rangeEnd != null) entryQ = entryQ.Where(x => x.v.PostedTime < rangeEnd);

        {
            var totals = await entryQ.GroupBy(_ => 1)
                .Select(g => new { Debit = g.Sum(x => x.e.Debit), Credit = g.Sum(x => x.e.Credit) })
                .FirstOrDefaultAsync();
            var debit = totals?.Debit ?? 0m;
            var credit = totals?.Credit ?? 0m;
            var unbalanced = debit != credit;

            // 投影 vs 分录（可选演示过滤：只比参与本次分录的子户）。entrySums 对触达子户求全部
            // 历史分录之和——投影本身是累计值，语义要求全历史，无时间界是有意为之
            var subIds = await entryQ.Select(x => x.e.SubAccountId).Distinct().ToListAsync();
            var projDiffs = await (
                from s in db.SubAccounts.AsNoTracking()
                where subIds.Contains(s.Id)
                join a in db.Accounts.AsNoTracking() on s.AccountId equals a.Id
                join p in db.BalanceProjections.AsNoTracking() on s.Id equals p.SubAccountId into pj
                from p in pj.DefaultIfEmpty()
                select new { s.Id, a.NormalSide, Proj = p != null ? p.Balance : 0m }).ToListAsync();

            var entrySums = await db.Entries.AsNoTracking().Where(e => subIds.Contains(e.SubAccountId))
                .GroupBy(e => e.SubAccountId)
                .Select(g => new { g.Key, D = g.Sum(e => e.Debit), C = g.Sum(e => e.Credit) })
                .ToDictionaryAsync(x => x.Key);

            var diffs = projDiffs.Select(x =>
            {
                entrySums.TryGetValue(x.Id, out var sum);
                var expected = sum == null ? 0m
                    : (x.NormalSide == Integration.Contracts.FinNormalSide.Debit ? sum.D - sum.C : sum.C - sum.D);
                return new { SubAccountId = x.Id, Expected = expected, Actual = x.Proj, Diff = x.Proj - expected };
            }).Where(d => d.Diff != 0m).ToList();

            var hasDiff = unbalanced || diffs.Count > 0;
            var run = new FinReconcileRun(GuidGenerator.Create(), string.IsNullOrWhiteSpace(input.RunType) ? "ledger-internal" : input.RunType, CurrentTenant.Id)
            {
                IncludeDemo = input.IncludeDemo,
                HasDiff = hasDiff,
                ScopeJson = JsonSerializer.Serialize(new { rangeStart = Iso(rangeStart), rangeEnd = Iso(rangeEnd), includeDemo = input.IncludeDemo }),
                DiffJson = JsonSerializer.Serialize(new { debit, credit, unbalanced, projectionDiffs = diffs }),
            };
            await db.ReconcileRuns.AddAsync(run);
            await db.SaveChangesAsync();
            await rrUow.CompleteAsync();
            return Map(run);
        }
    }

    /// <summary>入参须带 Z 或偏移；落库与查询都用 Kind=Unspecified 的 UTC 值（库列为 timestamp without time zone）。</summary>
    private static DateTime? ToUtc(DateTime? value, string field)
    {
        if (value == null) return null;
        if (value.Value.Kind == DateTimeKind.Unspecified)
        {
            throw new BusinessException(FinanceErrorCodes.ReconcileInvalidRange).WithData("Reason", field + " must be UTC with 'Z'");
        }

        return DateTime.SpecifyKind(value.Value.ToUniversalTime(), DateTimeKind.Unspecified);
    }

    private static string? Iso(DateTime? utc) => utc?.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private static FinReconcileRunDto Map(FinReconcileRun r) => new()
    {
        Id = r.Id,
        RunType = r.RunType,
        IncludeDemo = r.IncludeDemo,
        HasDiff = r.HasDiff,
        DiffJson = r.DiffJson,
        ScopeJson = r.ScopeJson,
        CreationTime = r.CreationTime,
    };
}
