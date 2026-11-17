using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Modules;

public interface IModuleManagementAppService : IApplicationService
{
    Task<ModuleStateListDto> GetAsync();
    Task<ModuleChangePreviewDto> PreviewAsync(ModuleChangeInput input);
    Task<ModuleStateListDto> UpdateAsync(ModuleChangeInput input);
}

public sealed class ModuleChangeInput
{
    [Required, StringLength(100)]
    public string ModuleId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    [Range(0, long.MaxValue)]
    public long ExpectedVersion { get; set; }
}

public sealed class ModuleStateListDto
{
    public long Version { get; set; }
    public bool Initialized { get; set; }
    public List<ModuleStateDto> Modules { get; set; } = [];
}

public sealed class ModuleStateDto
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public List<string> Dependencies { get; set; } = [];
    public string? ConfigurationPath { get; set; }
    public string? HistoryPath { get; set; }
}

public sealed class ModuleChangePreviewDto
{
    public long Version { get; set; }
    public bool CanApply { get; set; }
    public List<ModuleStateChangeDto> Changes { get; set; } = [];
    public List<string> BlockingModuleIds { get; set; } = [];
}

public sealed class ModuleStateChangeDto
{
    public string ModuleId { get; set; } = string.Empty;
    public bool WasEnabled { get; set; }
    public bool IsEnabled { get; set; }
}
