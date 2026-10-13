using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Domain.Repositories.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Localization;

public class EfCoreLanguageRepository : EfCoreRepository<AbpAdminDbContext, Language, Guid>, ILanguageRepository
{
    public EfCoreLanguageRepository(IDbContextProvider<AbpAdminDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<Language?> FindByCultureNameAsync(string cultureName, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.FirstOrDefaultAsync(x => x.CultureName == cultureName, cancellationToken);
    }

    public virtual async Task<List<Language>> GetEnabledListAsync(CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.Where(x => x.IsEnabled).OrderBy(x => x.DisplayName).ToListAsync(cancellationToken);
    }

    public virtual async Task<Language?> GetDefaultAsync(CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.FirstOrDefaultAsync(x => x.IsDefault, cancellationToken);
    }

    public virtual async Task<bool> ExistsAsync(string cultureName, CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AnyAsync(x => x.CultureName == cultureName, cancellationToken);
    }
}
