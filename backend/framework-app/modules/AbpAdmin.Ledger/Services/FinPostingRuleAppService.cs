using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Services;

/// <summary>
/// A2b 过账规则只读：GET /api/app/fin-posting-rule。
/// 默认排除 Deprecated（status=2），避免已废弃模板（如 demo.commission）出现在管理端列表；
/// 显式 IncludeDeprecated=true 时返回全部状态。
/// </summary>
[Authorize(FinancePermissions.Rule.View)]
public class FinPostingRuleAppService : FinanceAppServiceBase
{
    private readonly IDbContextProvider<LedgerDbContext> _db;
    private readonly IDataFilter _dataFilter;

    public FinPostingRuleAppService(IDbContextProvider<LedgerDbContext> db, IDataFilter dataFilter)
    {
        _db = db;
        _dataFilter = dataFilter;
    }

    public virtual async Task<ListResultDto<FinPostingRuleDto>> GetListAsync(FinPostingRuleListInput? input = null)
    {
        input ??= new FinPostingRuleListInput();
        var db = await _db.GetDbContextAsync();
        var tenantId = CurrentTenant.Id;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var q = db.PostingRules.AsNoTracking()
                .Where(r => r.TenantId == tenantId || r.TenantId == null);
            // 默认隐藏 Deprecated；历史凭证仍记着当时的 RuleVersion，列表不必展示废弃模板。
            if (!input.IncludeDeprecated)
            {
                q = q.Where(r => r.Status != FinPostingRuleStatus.Deprecated);
            }

            var rules = await q.OrderBy(r => r.BizType).ThenBy(r => r.Phase).ThenBy(r => r.Version)
                .ToListAsync();
            return new ListResultDto<FinPostingRuleDto>(rules.Select(r => new FinPostingRuleDto
            {
                Id = r.Id,
                BizType = r.BizType,
                Phase = r.Phase,
                Version = r.Version,
                Status = r.Status,
                TemplateJson = r.TemplateJson,
                Description = r.Description,
                IsHost = r.TenantId == null,
            }).ToList());
        }
    }
}
