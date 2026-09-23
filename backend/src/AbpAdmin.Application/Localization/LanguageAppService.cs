using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Caching;
using Volo.Abp.Domain.Repositories;

namespace AbpAdmin.Localization;

[Authorize(AbpAdminPermissions.Languages.Default)]
public class LanguageAppService : ApplicationService, ILanguageAppService
{
    private readonly ILanguageRepository _languageRepository;
    private readonly LanguageManager _languageManager;
    private readonly IDistributedCache<LanguageCacheItem> _languageCache;

    public LanguageAppService(
        ILanguageRepository languageRepository,
        LanguageManager languageManager,
        IDistributedCache<LanguageCacheItem> languageCache)
    {
        _languageRepository = languageRepository;
        _languageManager = languageManager;
        _languageCache = languageCache;
    }

    public virtual async Task<ListResultDto<LanguageDto>> GetListAsync()
    {
        var languages = await _languageRepository.GetListAsync();
        return new ListResultDto<LanguageDto>(
            ObjectMapper.Map<List<Language>, List<LanguageDto>>(languages));
    }

    public virtual async Task<LanguageDto> GetAsync(Guid id)
    {
        var language = await _languageRepository.GetAsync(id);
        return ObjectMapper.Map<Language, LanguageDto>(language);
    }

    [Authorize(AbpAdminPermissions.Languages.Create)]
    public virtual async Task<LanguageDto> CreateAsync(CreateLanguageDto input)
    {
        await _languageManager.ValidateCreateAsync(input.CultureName);

        var language = new Language(
            GuidGenerator.Create(),
            input.CultureName,
            input.UiCultureName,
            input.DisplayName,
            input.FlagIcon,
            input.IsEnabled,
            isDefault: false);

        await _languageRepository.InsertAsync(language);

        ClearLanguageListCacheAfterCommit();

        return ObjectMapper.Map<Language, LanguageDto>(language);
    }

    [Authorize(AbpAdminPermissions.Languages.Update)]
    public virtual async Task<LanguageDto> UpdateAsync(Guid id, UpdateLanguageDto input)
    {
        var language = await _languageRepository.GetAsync(id);

        // 验证不允许修改 CultureName 和 UiCultureName（这两个字段不在 UpdateDto 中，但防御性检查）
        await _languageManager.ValidateUpdateAsync(language, null, null);

        // 如果要禁用，验证不是默认语言
        if (!input.IsEnabled && language.IsEnabled)
        {
            await _languageManager.ValidateDisableAsync(language);
        }

        language.DisplayName = input.DisplayName;
        language.FlagIcon = input.FlagIcon;
        language.IsEnabled = input.IsEnabled;

        await _languageRepository.UpdateAsync(language);

        ClearLanguageListCacheAfterCommit();

        return ObjectMapper.Map<Language, LanguageDto>(language);
    }

    [Authorize(AbpAdminPermissions.Languages.Delete)]
    public virtual async Task DeleteAsync(Guid id)
    {
        var language = await _languageRepository.GetAsync(id);

        await _languageManager.ValidateDeleteAsync(language);

        await _languageRepository.DeleteAsync(language);

        ClearLanguageListCacheAfterCommit();
    }

    [Authorize(AbpAdminPermissions.Languages.ChangeDefault)]
    public virtual async Task SetAsDefaultAsync(Guid id)
    {
        await _languageManager.SetDefaultLanguageAsync(id);

        ClearLanguageListCacheAfterCommit();
    }

    /// <summary>
    /// 语言列表缓存（DbLanguageProvider 的 AllLanguages，30 分钟绝对过期）没有其他失效路径，
    /// 所有写操作（增/改/删/换默认）后必须清掉，否则语言切换器最长 30 分钟保持旧值。
    /// 挂在 UoW 提交后执行，避免"缓存已清、事务未提交"窗口里并发读把旧值回填进缓存。
    /// </summary>
    private void ClearLanguageListCacheAfterCommit()
    {
        UnitOfWorkManager.Current!.OnCompleted(async () =>
        {
            try
            {
                await _languageCache.RemoveAsync(LanguageCacheKeys.AllLanguages);
            }
            catch (Exception e)
            {
                // 此时事务已提交：清缓存失败只造成最长一个 TTL 的脏读，降级记日志，不应让客户端收到 500
                Logger.LogWarning(e, "语言列表缓存清除失败，切换器最长 30 分钟保持旧值");
            }
        });
    }
}
