using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.MultiTenancy;

namespace AbpAdmin.Ledger.Services;

/// <summary>A2b 过账规则只读：GET /api/app/fin-posting-rule</summary>
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

    public virtual async Task<ListResultDto<FinPostingRuleDto>> GetListAsync()
    {
        var db = await _db.GetDbContextAsync();
        var tenantId = CurrentTenant.Id;
        using (_dataFilter.Disable<IMultiTenant>())
        {
            var rules = await db.PostingRules.AsNoTracking()
                .Where(r => r.TenantId == tenantId || r.TenantId == null)
                .OrderBy(r => r.BizType).ThenBy(r => r.Phase).ThenBy(r => r.Version)
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
