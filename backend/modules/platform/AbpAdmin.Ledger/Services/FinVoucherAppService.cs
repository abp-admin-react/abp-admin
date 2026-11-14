using System;
using System.Collections.Generic;
using System.Globalization;
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
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;

namespace AbpAdmin.Ledger.Services;

/// <summary>查询和红冲凭证。总账手工调账红冲时同步调账状态，用户余额调账不允许单独红冲总账。</summary>
[Authorize(FinancePermissions.Voucher.View)]
public class FinVoucherAppService : FinanceAppServiceBase
{
    private readonly IDbContextProvider<LedgerDbContext> _dbContextProvider;
    private readonly LedgerPostingEngine _engine;
    private readonly PostingRuleProvider _rules;
    private readonly IRepository<IdentityUser, Guid> _users;

    public FinVoucherAppService(
        IDbContextProvider<LedgerDbContext> dbContextProvider,
        LedgerPostingEngine engine,
        PostingRuleProvider rules,
        IRepository<IdentityUser, Guid> users)
    {
        _dbContextProvider = dbContextProvider;
        _engine = engine;
        _rules = rules;
        _users = users;
    }

    public virtual async Task<PagedResultDto<FinVoucherDto>> GetListAsync(FinVoucherListInput input)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var q = db.Vouchers.AsNoTracking();
        if (input.VoucherId != null) q = q.Where(v => v.Id == input.VoucherId);
        if (input.UserId != null) q = q.Where(v => v.UserId == input.UserId);
        if (!string.IsNullOrWhiteSpace(input.Email))
        {
            var normalizedEmail = input.Email.Trim().ToUpperInvariant();
            var userIds = await (await _users.GetQueryableAsync())
                .Where(user => user.NormalizedEmail == normalizedEmail)
                .Select(user => user.Id).ToListAsync();
            q = q.Where(v => v.UserId != null && userIds.Contains(v.UserId.Value));
        }
        var statuses = ParseStatuses<FinVoucherStatus>(input.Status);
        if (statuses != null)
        {
            q = q.Where(v => statuses.Contains(v.Status));
        }

        // 代理范围优先于客户端过滤，防止按其他代理 ID 查询。
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
        var entryTotals = await db.Entries.AsNoTracking()
            .Where(entry => ids.Contains(entry.VoucherId))
            .GroupBy(entry => entry.VoucherId)
            .Select(group => new { Id = group.Key, Count = group.Count(), Debit = group.Sum(entry => entry.Debit), Credit = group.Sum(entry => entry.Credit) })
            .ToDictionaryAsync(group => group.Id);
        var reversedBy = await db.Vouchers.AsNoTracking().Where(v => v.ReversalOf != null && ids.Contains(v.ReversalOf.Value))
            .ToDictionaryAsync(v => v.ReversalOf!.Value, v => v.Id);
        // 批量读取当前页的会计期间，避免逐笔查询。
        var periodIds = items.Where(v => v.PeriodId != null).Select(v => v.PeriodId!.Value).Distinct().ToList();
        var periodLabels = await db.Periods.AsNoTracking().Where(p => periodIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => PeriodLabel(p.Year, p.Month));
        return new PagedResultDto<FinVoucherDto>(total, items.Select(v =>
        {
            var dto = Map(v, reversedBy.GetValueOrDefault(v.Id));
            if (entryTotals.TryGetValue(v.Id, out var totals))
            {
                dto.EntryCount = totals.Count;
                dto.TotalDebit = totals.Debit;
                dto.TotalCredit = totals.Credit;
                dto.TotalDebitText = Money(totals.Debit);
                dto.TotalCreditText = Money(totals.Credit);
            }
            dto.PeriodLabel = v.PeriodId != null ? periodLabels.GetValueOrDefault(v.PeriodId!.Value) : null;
            return dto;
        }).ToList());
    }

    /// <summary>详情和红冲对代理范围外的凭证返回 VoucherNotFound，避免暴露凭证是否存在。</summary>
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
            dto.PeriodLabel = period != null ? PeriodLabel(period.Year, period.Month) : null;
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
            DebitText = Money(r.e.Debit),
            CreditText = Money(r.e.Credit),
            ChangeAmountText = Money(accounts[r.s.AccountId].NormalSide == FinNormalSide.Debit
                ? r.e.Debit - r.e.Credit
                : r.e.Credit - r.e.Debit),
            BalanceAfterText = r.e.BalanceAfter is { } balanceAfter ? Money(balanceAfter) : null,
            Dimensions = r.e.Dimensions,
        }).ToList();
        dto.TotalDebit = dto.Entries.Sum(e => e.Debit);
        dto.TotalCredit = dto.Entries.Sum(e => e.Credit);
        dto.TotalDebitText = Money(dto.TotalDebit);
        dto.TotalCreditText = Money(dto.TotalCredit);
        dto.EntryCount = dto.Entries.Count;
        return dto;
    }

    /// <summary>对允许红冲的凭证返回对应红字凭证，重复请求返回原红字凭证。</summary>
    [Authorize(FinancePermissions.Voucher.Reverse)]
    public virtual async Task<FinVoucherDto> ReverseAsync(Guid id)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var v = await GetScopedVoucherAsync(db, id);
        if (v.BizType == FinanceBizTypes.WalletAdjustment)
        {
            throw new BusinessException(FinanceErrorCodes.VoucherNotReversible).WithData("VoucherId", id);
        }
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
        AmountText = Money(v.Amount),
        // 初始值使用业务金额，列表和详情随后按实际分录覆盖借贷合计。
        TotalDebit = v.Amount,
        TotalCredit = v.Amount,
        TotalDebitText = Money(v.Amount),
        TotalCreditText = Money(v.Amount),
        CreatorId = v.CreatorId,
        CreationTime = v.CreationTime,
    };

    private static string PeriodLabel(int year, int month) => year.ToString("0000") + "-" + month.ToString("00");

    private static string Money(decimal value) => value.ToString("0.##################", CultureInfo.InvariantCulture);
}
