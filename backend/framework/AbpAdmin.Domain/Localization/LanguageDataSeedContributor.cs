using System.Threading.Tasks;
using Volo.Abp.Data;
using Volo.Abp.DependencyInjection;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Uow;

namespace AbpAdmin.Localization;

/// <summary>
/// 语言数据种子贡献者。只在 host 上下文执行，租户上下文直接返回。
/// 补缺逻辑在 LanguageManager.SyncMissingFromStaticAsync（与 LanguageSyncJobHandler 共用）：
/// 只插缺失的，不更新已存在的，不删除多余的；若数据库中没有任何 IsDefault = true 的记录，把 zh-Hans 设为默认。
/// </summary>
public class LanguageDataSeedContributor : IDataSeedContributor, ITransientDependency
{
    private readonly LanguageManager _languageManager;
    private readonly ICurrentTenant _currentTenant;

    public LanguageDataSeedContributor(
        LanguageManager languageManager,
        ICurrentTenant currentTenant)
    {
        _languageManager = languageManager;
        _currentTenant = currentTenant;
    }

    [UnitOfWork]
    public virtual async Task SeedAsync(DataSeedContext context)
    {
        // 只在 host 上下文执行
        if (_currentTenant.Id.HasValue)
        {
            return;
        }

        await _languageManager.SyncMissingFromStaticAsync(markDefaultIfNone: true);
    }
}
