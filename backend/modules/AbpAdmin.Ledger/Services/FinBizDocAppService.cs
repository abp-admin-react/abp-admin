using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Application.Dtos;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Services;

/// <summary>
/// A6 业务单据只读：按凭证头投影（无下游时演示单据 IsDemo / demo.*）。
/// agentId 可筛；持 Finance.Scope.Agent 时强制当前用户 Id。
/// </summary>
[Authorize(FinancePermissions.BizDoc.View)]
public class FinBizDocAppService : FinanceAppServiceBase
{
    private readonly IDbContextProvider<LedgerDbContext> _db;

    public FinBizDocAppService(IDbContextProvider<LedgerDbContext> db) => _db = db;

    public virtual async Task<PagedResultDto<FinVoucherDto>> GetListAsync(FinBizDocListInput input)
    {
        var agentScoped = await AuthorizationService.IsGrantedAsync(FinancePermissions.Scope.Agent);
        var agentId = agentScoped ? RequireUserId() : input.AgentId;

        var db = await _db.GetDbContextAsync();
        var q = db.Vouchers.AsNoTracking().Where(v => v.ReversalOf == null); // 业务视角：正本
        if (agentId != null) q = q.Where(v => v.AgentId == agentId);
        if (input.IsDemo != null) q = q.Where(v => v.IsDemo == input.IsDemo);
        if (!string.IsNullOrWhiteSpace(input.BizType)) q = q.Where(v => v.BizType == input.BizType);
        if (!input.IncludeDeprecated)
        {
            var tenantId = CurrentTenant.Id;
            using (DataFilter.Disable<IMultiTenant>())
            {
                var rules = await db.PostingRules.AsNoTracking()
                    .Where(r => r.TenantId == tenantId || r.TenantId == null)
                    .Select(r => new { r.BizType, r.Status })
                    .ToListAsync();
                var deprecatedOnly = rules.GroupBy(r => r.BizType)
                    .Where(g => g.All(r => r.Status == FinPostingRuleStatus.Deprecated))
                    .Select(g => g.Key).ToList();
                if (deprecatedOnly.Count > 0) q = q.Where(v => !deprecatedOnly.Contains(v.BizType));
            }
        }
        if (input.StartTime != null) q = q.Where(v => v.CreationTime >= input.StartTime);
        if (input.EndTime != null) q = q.Where(v => v.CreationTime < input.EndTime);
        if (!string.IsNullOrWhiteSpace(input.Filter))
            q = q.Where(v => v.BizRefId.Contains(input.Filter) || (v.Summary != null && v.Summary.Contains(input.Filter)));

        var total = await q.CountAsync();
        var (skip, take) = Paging(input);
        var items = await q.OrderByDescending(v => v.CreationTime).Skip(skip).Take(take).ToListAsync();
        return new PagedResultDto<FinVoucherDto>(total, items.Select(v => new FinVoucherDto
        {
            Id = v.Id, BizType = v.BizType, BizRefId = v.BizRefId, Phase = v.Phase, BatchKey = v.BatchKey,
            Status = v.Status, AgentId = v.AgentId, UserId = v.UserId, IsDemo = v.IsDemo, Summary = v.Summary,
            Currency = v.Currency, Amount = v.Amount, CreationTime = v.CreationTime, CreatorId = v.CreatorId,
            TotalDebit = v.Amount, TotalCredit = v.Amount,
        }).ToList());
    }
}
