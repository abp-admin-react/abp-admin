using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AbpAdmin.TextTemplates;

public interface ITextTemplateAppService : IApplicationService
{
    /// <summary>模板定义列表；filter 按 Name/显示名模糊过滤（不区分大小写）。</summary>
    Task<List<TextTemplateDto>> GetListAsync(string? filter = null);

    Task<TextTemplateDto> GetContentAsync(string name, string? cultureName = null);

    Task UpdateAsync(UpdateTextTemplateDto input);

    Task RestoreToDefaultAsync(RestoreTextTemplateToDefaultInput input);
}

public class TextTemplateDto
{
    public string Name { get; set; } = default!;

    public string? DisplayName { get; set; }

    public bool IsLayout { get; set; }

    public string? Layout { get; set; }

    public string? CultureName { get; set; }

    public string? Content { get; set; }

    /// <summary>
    /// 是否为沙箱引擎（如 Scriban）。非沙箱引擎（如 Razor）可以执行任意代码，需要额外权限。
    /// </summary>
    public bool IsSandboxed { get; set; }
}

/// <summary>
/// 文化名校验：与实体 TextTemplateContent 的 128 位 Name 约束对齐，
/// CultureName 拒绝任意字符串（会直插列并进缓存 key）。
/// </summary>
public class UpdateTextTemplateDto
{
    /// <summary>模板名（与模板定义对齐，最大 128）。</summary>
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = default!;

    /// <summary>文化名；null 表示文化无关。</summary>
    [StringLength(16)]
    [RegularExpression(@"^[a-zA-Z]{2,3}(-[a-zA-Z0-9-]+)?$", ErrorMessage = "Invalid culture name.")]
    public string? CultureName { get; set; }

    [Required]
    public string Content { get; set; } = default!;
}

public class RestoreTextTemplateToDefaultInput
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = default!;

    [StringLength(16)]
    [RegularExpression(@"^[a-zA-Z]{2,3}(-[a-zA-Z0-9-]+)?$", ErrorMessage = "Invalid culture name.")]
    public string? CultureName { get; set; }
}
