using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.Localization;

public interface ILanguageTextRepository : IRepository<LanguageText, Guid>
{
    Task<LanguageText?> FindAsync(
        Guid? tenantId,
        string resourceName,
        string cultureName,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定上下文（tenantId 显式传入：host = null，租户 = 租户 Id）+ 资源 + 文化下的全部覆盖行。
    /// 注意：过滤（Filter/OnlyEmpty）不在仓储层做——列表的过滤口径是"合并后的生效值"，
    /// 只有应用层（合并静态基线之后）才算得出来，见 LanguageTextAppService。
    /// </summary>
    Task<List<LanguageText>> GetListAsync(
        Guid? tenantId,
        string resourceName,
        string cultureName,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid? tenantId,
        string resourceName,
        string cultureName,
        string name,
        CancellationToken cancellationToken = default);
}
