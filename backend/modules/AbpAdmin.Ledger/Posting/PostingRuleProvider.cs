using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Posting;

/// <summary>模板行：借方/贷方科目编码；"*" 表示取事件 From（借）/ To（贷）。</summary>
public sealed record PostingTemplateLine(string Debit, string Credit, string? Note, bool RequireAgentId);

public sealed record PostingRule(Guid Id, string BizType, string Phase, int Version, IReadOnlyList<PostingTemplateLine> Lines);

/// <summary>
/// 规则与科目查找：先当前租户自有，再回落宿主（种子只播在宿主）。
/// 规则按 (BizType, Phase) 精确匹配，取 Approved 的最高 Version；BatchKey 不参与定位。
/// </summary>
public class PostingRuleProvider : ITransientDependency
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IDbContextProvider<LedgerDbContext> _dbContextProvider;
    private readonly IDataFilter _dataFilter;
    private readonly ICurrentTenant _currentTenant;

    public PostingRuleProvider(
        IDbContextProvider<LedgerDbContext> dbContextProvider,
        IDataFilter dataFilter,
        ICurrentTenant currentTenant)
    {
        _dbContextProvider = dbContextProvider;
        _dataFilter = dataFilter;
        _currentTenant = currentTenant;
    }

    public virtual async Task<PostingRule?> FindAsync(string bizType, string phase)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var tenantId = _currentTenant.Id;
        FinPostingRule? rule;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            rule = await db.PostingRules.AsNoTracking()
                .Where(r => r.BizType == bizType && r.Phase == phase && r.Status == FinPostingRuleStatus.Approved
                            && (r.TenantId == tenantId || r.TenantId == null))
                .OrderBy(r => r.TenantId == null ? 1 : 0)
                .ThenByDescending(r => r.Version)
                .FirstOrDefaultAsync();
        }

        return rule == null ? null : new PostingRule(rule.Id, rule.BizType, rule.Phase, rule.Version, Parse(rule.TemplateJson));
    }

    public virtual async Task<FinAccount> GetAccountAsync(string code)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var tenantId = _currentTenant.Id;
        FinAccount? account;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            account = await db.Accounts.AsNoTracking()
                .Where(a => a.Code == code && (a.TenantId == tenantId || a.TenantId == null))
                .OrderBy(a => a.TenantId == null ? 1 : 0)
                .FirstOrDefaultAsync();
        }

        if (account == null || !account.IsActive)
        {
            throw new BusinessException(FinanceErrorCodes.AccountNotFound).WithData("AccountCode", code);
        }

        return account;
    }

    public virtual async Task<Dictionary<Guid, FinAccount>> GetAccountsByIdAsync(IEnumerable<Guid> ids)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var set = ids.Distinct().ToList();
        using (_dataFilter.Disable<IMultiTenant>())
        {
            return await db.Accounts.AsNoTracking().Where(a => set.Contains(a.Id)).ToDictionaryAsync(a => a.Id);
        }
    }

    public static IReadOnlyList<PostingTemplateLine> Parse(string templateJson)
    {
        var raw = JsonSerializer.Deserialize<List<RawLine>>(templateJson, JsonOptions) ?? new List<RawLine>();
        if (raw.Count == 0 || raw.Any(l => string.IsNullOrWhiteSpace(l.Debit) || string.IsNullOrWhiteSpace(l.Credit)))
        {
            throw new BusinessException(FinanceErrorCodes.PostingRuleNotFound).WithData("Reason", "empty or malformed template");
        }

        return raw.Select(l => new PostingTemplateLine(l.Debit!, l.Credit!, l.Note, l.RequireAgentId)).ToList();
    }

    private sealed class RawLine
    {
        public string? Debit { get; set; }
        public string? Credit { get; set; }
        public string? Note { get; set; }
        public bool RequireAgentId { get; set; }
    }
}
