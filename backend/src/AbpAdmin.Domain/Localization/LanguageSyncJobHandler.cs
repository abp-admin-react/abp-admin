using System.Threading.Tasks;
using AbpAdmin.ScheduledJobs;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Localization;

/// <summary>
/// 语言同步（T3.3 第 6 步，JobType = AbpAdmin.LanguageSync）。
/// 把 AbpLocalizationOptions.Languages 里注册的语言补进数据库（只插缺失，不更新不删除），
/// 覆盖"新部署加了语言但 DbMigrator 没重跑"的场景。补缺逻辑在 LanguageManager.SyncMissingFromStaticAsync
/// （与 LanguageDataSeedContributor 同一实现，插入后顺带清语言列表缓存）。
/// 语言是 host 级数据，租户级作业直接返回。
/// </summary>
[ExposeServices(typeof(IScheduledJobHandler))]
public class LanguageSyncJobHandler : IScheduledJobHandler, ITransientDependency
{
    public const string JobTypeName = "AbpAdmin.LanguageSync";

    public string JobType => JobTypeName;

    public string DisplayNameKey => "ScheduledJobType:LanguageSync";

    private readonly LanguageManager _languageManager;

    public LanguageSyncJobHandler(LanguageManager languageManager)
    {
        _languageManager = languageManager;
    }

    public virtual async Task ExecuteAsync(ScheduledJobContext context)
    {
        if (context.TenantId.HasValue)
        {
            return;
        }

        // 同步场景不设默认语言（保持种子之外库里已有的默认标记不动）
        await _languageManager.SyncMissingFromStaticAsync(markDefaultIfNone: false);
    }
}
