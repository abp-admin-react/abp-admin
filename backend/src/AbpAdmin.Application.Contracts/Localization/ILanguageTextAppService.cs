using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Localization;

public interface ILanguageTextAppService : IApplicationService
{
    /// <summary>
    /// 语言文本列表：静态基线 + 数据库覆盖的合并视图（对标 Pro 的静态文本外部存储）。
    /// CultureName 必选；ResourceName 可选（空 = 跨全部注册资源列出）。
    /// 两个口径：Value=生效值（本层覆盖 &gt; host 覆盖 &gt; 静态基线；静态基线沿目标文化父链，
    /// 刻意不含框架 DefaultCulture 回退——链上缺失的 key 显示空串并被 OnlyEmpty 计为未翻译）；
    /// IsOverridden=当前上下文是否有覆盖行（不含 host 的行，「恢复默认」的按钮依据）。
    /// </summary>
    Task<PagedResultDto<LanguageTextDto>> GetListAsync(GetLanguageTextsInput input);

    /// <summary>
    /// 写入/更新当前上下文的覆盖行（空值 = 显式标记未翻译）；返回覆盖行口径的 DTO。
    /// ResourceName 必须是已注册资源、CultureName 必须是合法文化，否则业务异常（fail-closed：
    /// 孤儿覆盖行在跨资源列表里不可见、不可管理，写入口是唯一闸门）。
    /// </summary>
    Task<LanguageTextDto> UpdateAsync(UpdateLanguageTextDto input);

    /// <summary>恢复默认 = 仅删除当前上下文的覆盖行（host/租户各管各层），与列表 IsOverridden 同口径。</summary>
    Task RestoreToDefaultAsync(string resourceName, string cultureName, string name);

    /// <summary>注册资源名列表（与列表「空资源 = 全部」同一宇宙，来自 AbpLocalizationOptions）。</summary>
    Task<ListResultDto<string>> GetResourceNamesAsync();
}
