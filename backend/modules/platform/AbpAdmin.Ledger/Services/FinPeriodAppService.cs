using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Permissions;
using AbpAdmin.Ledger.Services.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Ledger.Services;

/// <summary>A4 期间：GET /api/app/fin-period/current（本阶段无关账）。</summary>
[Authorize(FinancePermissions.Period.View)]
public class FinPeriodAppService : FinanceAppServiceBase
{
    private readonly IDbContextProvider<LedgerDbContext> _db;

    public FinPeriodAppService(IDbContextProvider<LedgerDbContext> db) => _db = db;

    public virtual async Task<FinPeriodDto> GetCurrentAsync()
    {
        var db = await _db.GetDbContextAsync();
        var now = Clock.Now;
        var period = await db.Periods.AsNoTracking()
            .Where(p => p.Year == now.Year && p.Month == now.Month)
            .FirstOrDefaultAsync();
        if (period == null)
        {
            // 种子应已有；兜底读最近 Open
            period = await db.Periods.AsNoTracking()
                .Where(p => p.Status == FinPeriodStatus.Open)
                .OrderByDescending(p => p.Year).ThenByDescending(p => p.Month)
                .FirstOrDefaultAsync()
                ?? throw new BusinessException(FinanceErrorCodes.InvalidEvent).WithData("Field", "no open period");
        }

        return new FinPeriodDto
        {
            Id = period.Id,
            Year = period.Year,
            Month = period.Month,
            Status = period.Status,
            IsCurrent = period.Year == now.Year && period.Month == now.Month,
        };
    }
}
