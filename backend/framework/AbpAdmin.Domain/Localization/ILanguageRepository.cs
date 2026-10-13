using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.Localization;

public interface ILanguageRepository : IRepository<Language, Guid>
{
    Task<Language?> FindByCultureNameAsync(string cultureName, CancellationToken cancellationToken = default);

    Task<List<Language>> GetEnabledListAsync(CancellationToken cancellationToken = default);

    Task<Language?> GetDefaultAsync(CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string cultureName, CancellationToken cancellationToken = default);
}
