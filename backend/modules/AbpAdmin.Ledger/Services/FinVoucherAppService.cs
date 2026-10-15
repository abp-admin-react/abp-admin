using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Posting;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Ledger.Services;

/// <summary>A3 凭证（只读 + 红冲）。自动凭证不可改，只能红冲；调账凭证红冲时同步调账单 → Reversed。</summary>
[Authorize(FinancePermissions.Voucher.View)]
public class FinVoucherAppService : FinanceAppServiceBase
{
    private readonly IDbContextProvider<LedgerDbContext> _dbContextProvider;
    private readonly LedgerPostingEngine _engine;
    private readonly PostingRuleProvider _rules;

    public FinVoucherAppService(
        IDbContextProvider<LedgerDbContext> dbContextProvider,
        LedgerPostingEngine engine,
        PostingRuleProvider rules)
    {
        _dbContextProvider = dbContextProvider;
        _engine = engine;
        _rules = rules;
    }

    public virtual async Task<PagedResultDto<FinVoucherDto>> GetListAsync(FinVoucherListInput input)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var q = db.Vouchers.AsNoTracking();
        var statuses = ParseStatuses<FinVoucherStatus>(input.Status);
        if (statuses != null)
        {
            q = q.Where(v => statuses.Contains(v.Status));
        }

        // 代理数据边界（与 A6/A7 同口径）：持 Scope.Agent 只能看自己的凭证，客户端传参不改写边界
        var agentScope = await ResolveAgentScopeAsync();
        if (agentScope != null) q = q.Where(v => v.AgentId == agentScope);
        else if (input.AgentId != null) q = q.Where(v => v.AgentId == input.AgentId);
        if (input.IsDemo != null) q = q.Where(v => v.IsDemo == input.IsDemo);
        if (!string.IsNullOrWhiteSpace(input.BizType)) q = q.Where(v => v.BizType == input.BizType);
        if (!string.IsNullOrWhiteSpace(input.BizRefId)) q = q.Where(v => v.BizRefId == input.BizRefId);
        if (!string.IsNullOrWhiteSpace(input.Phase)) q = q.Where(v => v.Phase == input.Phase);
        if (input.StartTime != null) q = q.Where(v => v.CreationTime >= input.StartTime);
        if (input.EndTime != null) q = q.Where(v => v.CreationTime < input.EndTime);
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            q = q.Where(v => v.BizRefId.Contains(input.Filter) || (v.Summary != null && v.Summary.Contains(input.Filter)));
        }

        var total = await q.CountAsync();
        var (skip, take) = Paging(input);
        var items = await q.OrderByDescending(v => v.CreationTime).ThenByDescending(v => v.Id).Skip(skip).Take(take).ToListAsync();
        var ids = items.Select(v => v.Id).ToList();
        var reversedBy = await db.Vouchers.AsNoTracking().Where(v => v.ReversalOf != null && ids.Contains(v.ReversalOf.Value))
            .ToDictionaryAsync(v => v.ReversalOf!.Value, v => v.Id);
        // 展示列补齐：凭证号已在 Map 按 Id 派生（单一出处，列表/详情/红冲一致）；
        // 期间标签需联期间表，在此整页取齐
        var periodIds = items.Where(v => v.PeriodId != null).Select(v => v.PeriodId!.Value).Distinct().ToList();
        var periodLabels = await db.Periods.AsNoTracking().Where(p => periodIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Year.ToString("0000") + "-" + p.Month.ToString("00"));
        return new PagedResultDto<FinVoucherDto>(total, items.Select(v =>
        {
            var dto = Map(v, reversedBy.GetValueOrDefault(v.Id));
            dto.PeriodLabel = v.PeriodId != null ? periodLabels.GetValueOrDefault(v.PeriodId!.Value) : null;
            return dto;
        }).ToList());
    }

    /// <summary>按 id 取凭证并套代理边界：范围外一律 VoucherNotFound（不泄露跨属主存在性）。
    /// 详情与红冲共用同一条边界规则，收在此处避免两份逐字复制漂移。</summary>
    private async Task<FinVoucher> GetScopedVoucherAsync(LedgerDbContext db, Guid id)
    {
        var v = await db.Vouchers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id)
                ?? throw new BusinessException(FinanceErrorCodes.VoucherNotFound).WithData("VoucherId", id);
        var agentScope = await ResolveAgentScopeAsync();
        if (agentScope != null && v.AgentId != agentScope)
        {
            throw new BusinessException(FinanceErrorCodes.VoucherNotFound).WithData("VoucherId", id);
        }

        return v;
    }

    public virtual async Task<FinVoucherDto> GetAsync(Guid id)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var v = await GetScopedVoucherAsync(db, id);
        var reversedBy = await db.Vouchers.AsNoTracking().Where(x => x.ReversalOf == id).Select(x => (Guid?)x.Id).FirstOrDefaultAsync();
        var dto = Map(v, reversedBy);
        if (v.PeriodId != null)
        {
            var period = await db.Periods.AsNoTracking().FirstOrDefaultAsync(p => p.Id == v.PeriodId.Value);
            dto.PeriodLabel = period != null ? period.Year.ToString("0000") + "-" + period.Month.ToString("00") : null;
        }
        var rows = await (
            from e in db.Entries.AsNoTracking()
            join s in db.SubAccounts.AsNoTracking() on e.SubAccountId equals s.Id
            where e.VoucherId == id
            orderby e.Seq
            select new { e, s }).ToListAsync();
        var accounts = await _rules.GetAccountsByIdAsync(rows.Select(r => r.s.AccountId));
        dto.Entries = rows.Select(r => new FinEntryDto
        {
            Seq = r.e.Seq,
            AccountCode = accounts[r.s.AccountId].Code,
            AccountName = accounts[r.s.AccountId].Name,
            OwnerType = r.s.OwnerType,
            OwnerId = r.s.OwnerId,
            Bucket = r.s.Bucket,
            Debit = r.e.Debit,
            Credit = r.e.Credit,
            BalanceAfter = r.e.BalanceAfter,
            Dimensions = r.e.Dimensions,
        }).ToList();
        dto.TotalDebit = dto.Entries.Sum(e => e.Debit);
        dto.TotalCredit = dto.Entries.Sum(e => e.Credit);
        return dto;
    }

    /// <summary>红冲：返回红字凭证。重复红冲返回同一张。</summary>
    [Authorize(FinancePermissions.Voucher.Reverse)]
    public virtual async Task<FinVoucherDto> ReverseAsync(Guid id)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var v = await GetScopedVoucherAsync(db, id);
        Guid reversalId;
        if (v.BizType == FinanceBizTypes.ManualAdjust && Guid.TryParseExact(v.BizRefId, "N", out var adjustId))
        {
            var adjust = await db.ManualAdjusts.FirstOrDefaultAsync(a => a.Id == adjustId);
            reversalId = await _engine.ReverseVoucherAsync(id);
            if (adjust is { Status: FinManualAdjustStatus.Posted })
            {
                adjust.MarkReversed(reversalId);
                await db.SaveChangesAsync();
            }
        }
        else
        {
            reversalId = await _engine.ReverseVoucherAsync(id);
        }

        return await GetAsync(reversalId);
    }

    private static FinVoucherDto Map(FinVoucher v, Guid? reversedBy) => new()
    {
        Id = v.Id,
        // 展示用凭证号（实体无此列，按 Id 派生）：派生规则单一出处在此——列表/详情/红冲
        // 三个端点形状一致，前端不做二次派生（DTO 注释「仅列表/详情展示」由此兑现）
        VoucherNo = "V-" + v.Id.ToString("N")[..10].ToUpperInvariant(),
        BizType = v.BizType,
        BizRefId = v.BizRefId,
        Phase = v.Phase,
        BatchKey = v.BatchKey,
        RuleVersion = v.RuleVersion,
        PeriodId = v.PeriodId,
        Status = v.Status,
        ReversalOf = v.ReversalOf,
        ReversedBy = reversedBy,
        AgentId = v.AgentId,
        UserId = v.UserId,
        IsDemo = v.IsDemo,
        Summary = v.Summary,
        Currency = v.Currency,
        Amount = v.Amount,
        TotalDebit = v.Amount,
        TotalCredit = v.Amount,
        CreatorId = v.CreatorId,
        CreationTime = v.CreationTime,
    };
}
