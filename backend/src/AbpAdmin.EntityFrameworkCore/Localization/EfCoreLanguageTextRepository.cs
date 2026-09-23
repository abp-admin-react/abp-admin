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

public class EfCoreLanguageTextRepository : EfCoreRepository<AbpAdminDbContext, LanguageText, Guid>, ILanguageTextRepository
{
    public EfCoreLanguageTextRepository(IDbContextProvider<AbpAdminDbContext> dbContextProvider)
        : base(dbContextProvider)
    {
    }

    public virtual async Task<LanguageText?> FindAsync(
        Guid? tenantId,
        string resourceName,
        string cultureName,
        string name,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId &&
            x.ResourceName == resourceName &&
            x.CultureName == cultureName &&
            x.Name == name,
            cancellationToken);
    }

    /// <summary>
    /// 指定上下文的全部覆盖行。唯一调用方是缓存回源（DbExternalLocalizationStore），
    /// 实体只作只读投影源——AsNoTracking 免掉 tracking/identity-map 开销；
    /// 需要追踪实体的路径走 FindAsync（写路径依赖它做 Update）。
    /// </summary>
    public virtual async Task<List<LanguageText>> GetListAsync(
        Guid? tenantId,
        string resourceName,
        string cultureName,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        return await dbSet.AsNoTracking().Where(x =>
            x.TenantId == tenantId &&
            x.ResourceName == resourceName &&
            x.CultureName == cultureName)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public virtual async Task DeleteAsync(
        Guid? tenantId,
        string resourceName,
        string cultureName,
        string name,
        CancellationToken cancellationToken = default)
    {
        var dbSet = await GetDbSetAsync();
        var entity = await dbSet.FirstOrDefaultAsync(x =>
            x.TenantId == tenantId &&
            x.ResourceName == resourceName &&
            x.CultureName == cultureName &&
            x.Name == name,
            cancellationToken);

        if (entity != null)
        {
            dbSet.Remove(entity);
        }
    }
}
