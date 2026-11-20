using AbpAdmin.Ledger.Entities;
using Microsoft.AspNetCore.Mvc;
using AbpAdmin.Grids;
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

    public virtual Task<PagedResultDto<FinVoucherDto>> GetListAsync(FinBizDocListInput input) =>
        ReadListPageAsync(null, input);

    [HttpPost("api/app/fin-biz-doc/query")]
    public virtual Task<PagedResultDto<FinVoucherDto>> QueryListAsync([FromBody] GridQueryInput grid, [FromQuery] FinBizDocListInput input, string? bizRefId = null) =>
        ReadListPageAsync(grid, input, bizRefId);

    private async Task<PagedResultDto<FinVoucherDto>> ReadListPageAsync(GridQueryInput? grid, FinBizDocListInput input, string? bizRefId = null)
    {
        // 代理数据边界统一走基类助手（替代与助手逐字等价的手写内联）
        var agentId = (await ResolveAgentScopeAsync()) ?? input.AgentId;

        var db = await _db.GetDbContextAsync();
        var q = db.Vouchers.AsNoTracking().Where(v => v.ReversalOf == null); // 业务视角：正本
        if (agentId != null) q = q.Where(v => v.AgentId == agentId);
        if (!string.IsNullOrWhiteSpace(bizRefId)) q = q.Where(v => v.BizRefId == bizRefId);
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

        var definition = new GridQueryDefinition<FinVoucher>()
            .SearchableField("id", x => x.Id.ToString())
            .SearchableField("userId", x => x.UserId.ToString())
            .SearchableField("bizRefId", x => x.BizRefId)
            .SearchableField("bizType", x => x.BizType)
            .SearchableField("phase", x => x.Phase)
            .SearchableEnumField("status", x => x.Status)
            .SearchableField("agentId", x => x.AgentId.ToString())
            .Field("isDemo", x => x.IsDemo)
            .Field("creationTime", x => x.CreationTime)
            .SearchableField("summary", x => x.Summary)
            .Field("amount", x => x.Amount);
        if (grid != null) q = definition.Filter(q, grid);
        var total = await q.CountAsync();
        var (skip, take) = grid == null ? Paging(input) : (grid.SkipCount, grid.MaxResultCount);
        var items = await (grid == null
            ? q.OrderByDescending(v => v.CreationTime)
            : definition.OrderBy(q, grid, "creationTime", true, x => x.Id)).Skip(skip).Take(take).ToListAsync();
        return new PagedResultDto<FinVoucherDto>(total, items.Select(v => new FinVoucherDto
        {
            Id = v.Id, BizType = v.BizType, BizRefId = v.BizRefId, Phase = v.Phase, BatchKey = v.BatchKey,
            Status = v.Status, AgentId = v.AgentId, UserId = v.UserId, IsDemo = v.IsDemo, Summary = v.Summary,
            Currency = v.Currency, Amount = v.Amount, CreationTime = v.CreationTime, CreatorId = v.CreatorId,
            TotalDebit = v.Amount, TotalCredit = v.Amount,
        }).ToList());
    }
}
