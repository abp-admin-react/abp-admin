using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Services;
using Volo.Abp.Guids;
using Volo.Abp.Localization;
using Volo.Abp.SettingManagement;
using Volo.Abp.Uow;

namespace AbpAdmin.Localization;

/// <summary>
/// 语言领域服务。处理默认语言设置、语言补缺同步等业务规则。
/// </summary>
public class LanguageManager : DomainService
{
    private readonly ILanguageRepository _languageRepository;
    private readonly ISettingManager _settingManager;
    private readonly IOptions<AbpLocalizationOptions> _localizationOptions;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IDistributedCache<LanguageCacheItem> _languageCache;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public LanguageManager(
        ILanguageRepository languageRepository,
        ISettingManager settingManager,
        IOptions<AbpLocalizationOptions> localizationOptions,
        IGuidGenerator guidGenerator,
        IDistributedCache<LanguageCacheItem> languageCache,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _languageRepository = languageRepository;
        _settingManager = settingManager;
        _localizationOptions = localizationOptions;
        _guidGenerator = guidGenerator;
        _languageCache = languageCache;
        _unitOfWorkManager = unitOfWorkManager;
    }

    /// <summary>
    /// 设置默认语言。同时更新设置项和 Language.IsDefault 标记。
    /// </summary>
    public virtual async Task SetDefaultLanguageAsync(Guid languageId)
    {
        // GetAsync 找不到时抛 EntityNotFoundException，不会返回 null
        var language = await _languageRepository.GetAsync(languageId);

        if (!language.IsEnabled)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Localization.CannotSetDisabledLanguageAsDefault)
                .WithData("CultureName", language.CultureName);
        }

        // 清除其他语言的默认标记
        var currentDefault = await _languageRepository.GetDefaultAsync();
        if (currentDefault != null && currentDefault.Id != languageId)
        {
            currentDefault.IsDefault = false;
            await _languageRepository.UpdateAsync(currentDefault);
        }

        // 设置新默认语言
        language.IsDefault = true;
        await _languageRepository.UpdateAsync(language);

        // 写入框架设置项
        await _settingManager.SetForCurrentTenantAsync(
            LocalizationSettingNames.DefaultLanguage,
            language.CultureName);
    }

    /// <summary>
    /// 把 AbpLocalizationOptions.Languages 里注册的语言补进数据库（只插缺失，不更新不删除）。
    /// LanguageDataSeedContributor（种子）与 LanguageSyncJobHandler（定时补数）共用同一实现。
    /// </summary>
    /// <param name="markDefaultIfNone">
    /// 数据库无任何 IsDefault = true 的记录时，把 zh-Hans 插成默认（种子侧传 true，
    /// 与 web/config/config.ts 的 locale.default: 'zh-CN' 对齐；同步 Job 维持原行为不设默认，传 false）。
    /// </param>
    /// <returns>新插入的语言数量。</returns>
    public virtual async Task<int> SyncMissingFromStaticAsync(bool markDefaultIfNone)
    {
        var staticLanguages = _localizationOptions.Value.Languages;
        var existingLanguages = await _languageRepository.GetListAsync();
        var existingCultureNames = existingLanguages.Select(x => x.CultureName).ToHashSet();

        // 只插入缺失的，不更新已存在的，不删除多余的
        // 注意：zh-Hans 的默认标记必须在插入时就带上。不能「插完再查库设默认」——
        // EF Core 的 LINQ 查询不返回同一 UoW 里 Added 未保存的行，FindByCultureNameAsync
        // 会拿到 null 而静默跳过（真实库里有默认行只是种子跑过第二遍的假象）。
        var inserted = 0;
        var hasDefault = existingLanguages.Any(x => x.IsDefault);
        foreach (var languageInfo in staticLanguages)
        {
            if (existingCultureNames.Contains(languageInfo.CultureName))
            {
                continue;
            }

            var isDefault = markDefaultIfNone && !hasDefault && languageInfo.CultureName == "zh-Hans";

            await _languageRepository.InsertAsync(new Language(
                _guidGenerator.Create(),
                languageInfo.CultureName,
                languageInfo.UiCultureName,
                languageInfo.DisplayName,
                flagIcon: null,
                isEnabled: true,
                isDefault: isDefault));

            if (isDefault)
            {
                hasDefault = true;
            }

            inserted++;
        }

        if (inserted > 0)
        {
            // 语言列表缓存（DbLanguageProvider，30 分钟绝对过期）随之失效。
            // 挂到 UoW 提交后执行（AppService 侧写路径均已如此规避）：事务内直接清，
            // "缓存已清、事务未提交"窗口里并发读会把旧值回填进缓存；无 UoW 上下文（宿主直调）时立即清。
            var unitOfWork = _unitOfWorkManager.Current;
            if (unitOfWork != null)
            {
                unitOfWork.OnCompleted(async () =>
                {
                    try
                    {
                        await _languageCache.RemoveAsync(LanguageCacheKeys.AllLanguages);
                    }
                    catch (Exception e)
                    {
                        // 此时事务已提交：清缓存失败只造成最长一个 TTL 的脏读，降级记日志
                        Logger.LogWarning(e, "语言列表缓存清除失败，切换器最长 30 分钟保持旧值");
                    }
                });
            }
            else
            {
                await _languageCache.RemoveAsync(LanguageCacheKeys.AllLanguages);
            }
        }

        return inserted;
    }

    /// <summary>
    /// 验证创建语言请求。重复 CultureName 拒绝。
    /// </summary>
    public virtual async Task ValidateCreateAsync(string cultureName)
    {
        if (await _languageRepository.ExistsAsync(cultureName))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Localization.LanguageAlreadyExists)
                .WithData("CultureName", cultureName);
        }
    }

    /// <summary>
    /// 验证更新语言请求。不允许修改 CultureName 和 UiCultureName。
    /// </summary>
    public virtual async Task ValidateUpdateAsync(Language language, string? newCultureName, string? newUiCultureName)
    {
        if (newCultureName != null && newCultureName != language.CultureName)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Localization.CannotModifyCultureName)
                .WithData("Field", "CultureName");
        }

        if (newUiCultureName != null && newUiCultureName != language.UiCultureName)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Localization.CannotModifyCultureName)
                .WithData("Field", "UiCultureName");
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 验证删除语言请求。删除当前默认语言要拒绝。
    /// </summary>
    public virtual async Task ValidateDeleteAsync(Language language)
    {
        if (language.IsDefault)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Localization.CannotDeleteDefaultLanguage)
                .WithData("CultureName", language.CultureName);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 验证禁用语言请求。禁用当前默认语言要拒绝。
    /// </summary>
    public virtual async Task ValidateDisableAsync(Language language)
    {
        if (language.IsDefault)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Localization.CannotDisableDefaultLanguage)
                .WithData("CultureName", language.CultureName);
        }

        await Task.CompletedTask;
    }
}
