using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AbpAdmin.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.VirtualFileSystem;

namespace AbpAdmin.VirtualFileExplorer;

[Authorize(AbpAdminPermissions.VirtualFileExplorer.Default)]
public class VirtualFileExplorerAppService : AbpAdminAppService, IVirtualFileExplorerAppService
{
    private const int MaxPreviewBytes = 256 * 1024;
    private readonly IVirtualFileProvider _virtualFileProvider;

    public VirtualFileExplorerAppService(IVirtualFileProvider virtualFileProvider)
    {
        _virtualFileProvider = virtualFileProvider;
    }

    public virtual Task<VirtualFileDirectoryDto> GetListAsync(GetVirtualFileListInput input)
    {
        var path = Normalize(input.Path);
        var contents = _virtualFileProvider.GetDirectoryContents(path);
        var items = contents
            .OrderByDescending(x => x.IsDirectory)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new VirtualFileItemDto
            {
                Name = x.Name,
                Path = Combine(path, x.Name),
                IsDirectory = x.IsDirectory,
                Exists = x.Exists,
                Length = x.Length
            })
            .ToList();

        return Task.FromResult(new VirtualFileDirectoryDto
        {
            Path = path,
            ParentPath = GetParent(path),
            Items = items
        });
    }

    public virtual async Task<VirtualFileContentDto> GetContentAsync(GetVirtualFileContentInput input)
    {
        var path = Normalize(input.Path);
        var file = _virtualFileProvider.GetFileInfo(path);
        if (!file.Exists || file.IsDirectory)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.VirtualFileExplorer.FileNotFound)
                .WithData("Path", path);
        }

        var dto = new VirtualFileContentDto
        {
            Name = file.Name,
            Path = path,
            Length = file.Length
        };

        if (file.Length > MaxPreviewBytes)
        {
            dto.Truncated = true;
            return dto;
        }

        await using var stream = file.CreateReadStream();
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory);
        var bytes = memory.ToArray();
        if (IsBinary(bytes))
        {
            dto.IsBinary = true;
            return dto;
        }

        dto.Content = Encoding.UTF8.GetString(bytes);
        return dto;
    }

    private static string Normalize(string? path)
    {
        path = (path ?? "/").Replace('\\', '/');
        if (!path.StartsWith('/'))
        {
            path = "/" + path;
        }

        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x != "." && x != "..");
        var normalized = "/" + string.Join("/", parts);
        return normalized == "/" ? "/" : normalized;
    }

    private static string Combine(string parent, string name)
    {
        return parent == "/" ? "/" + name : parent.TrimEnd('/') + "/" + name;
    }

    private static string? GetParent(string path)
    {
        if (path == "/")
        {
            return null;
        }

        var index = path.LastIndexOf('/');
        return index <= 0 ? "/" : path[..index];
    }

    private static bool IsBinary(byte[] bytes)
    {
        var take = Math.Min(bytes.Length, 512);
        for (var i = 0; i < take; i++)
        {
            if (bytes[i] == 0)
            {
                return true;
            }
        }

        return false;
    }
}
