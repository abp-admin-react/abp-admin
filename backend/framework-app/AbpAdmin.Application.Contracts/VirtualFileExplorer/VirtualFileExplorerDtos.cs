using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace AbpAdmin.VirtualFileExplorer;

public interface IVirtualFileExplorerAppService : IApplicationService
{
    Task<VirtualFileDirectoryDto> GetListAsync(GetVirtualFileListInput input);

    Task<VirtualFileContentDto> GetContentAsync(GetVirtualFileContentInput input);
}

public class GetVirtualFileListInput
{
    public string? Path { get; set; }
}

public class GetVirtualFileContentInput
{
    public string Path { get; set; } = "/";
}

public class VirtualFileDirectoryDto
{
    public string Path { get; set; } = "/";

    public string? ParentPath { get; set; }

    public List<VirtualFileItemDto> Items { get; set; } = new();
}

public class VirtualFileItemDto
{
    public string Name { get; set; } = default!;

    public string Path { get; set; } = default!;

    public bool IsDirectory { get; set; }

    public bool Exists { get; set; }

    public long Length { get; set; }
}

public class VirtualFileContentDto
{
    public string Name { get; set; } = default!;

    public string Path { get; set; } = default!;

    public long Length { get; set; }

    public bool IsBinary { get; set; }

    public bool Truncated { get; set; }

    public string? Content { get; set; }
}
