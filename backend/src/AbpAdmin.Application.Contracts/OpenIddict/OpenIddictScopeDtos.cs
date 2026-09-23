using System;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace AbpAdmin.OpenIddict;

public interface IOpenIddictScopeAppService : IApplicationService
{
    Task<PagedResultDto<OpenIddictScopeDto>> GetListAsync(PagedAndSortedResultRequestDto input);

    Task<ListResultDto<OpenIddictScopeLookupDto>> GetAllAsync();

    Task<OpenIddictScopeDto> CreateAsync(CreateOpenIddictScopeDto input);

    Task<OpenIddictScopeDto> UpdateAsync(Guid id, UpdateOpenIddictScopeDto input);

    Task DeleteAsync(Guid id);
}

public class OpenIddictScopeDto : EntityDto<Guid>
{
    public string? Name { get; set; }

    public string? DisplayName { get; set; }

    public string? Description { get; set; }

    public string? Resources { get; set; }
}

/// <summary>scope 下拉列表项：托管 scope（数据库记录）+ 内置 scope（IsBuiltIn=true）。</summary>
public class OpenIddictScopeLookupDto
{
    public string Name { get; set; } = default!;

    public string? DisplayName { get; set; }

    /// <summary>内置 scope 不是数据库记录，不可编辑/删除。</summary>
    public bool IsBuiltIn { get; set; }
}

public class CreateOpenIddictScopeDto
{
    [Required]
    [StringLength(200)]
    public string Name { get; set; } = default!;

    [StringLength(400)]
    public string? DisplayName { get; set; }

    [StringLength(1000)]
    public string? Description { get; set; }

    public string? Resources { get; set; }
}

public class UpdateOpenIddictScopeDto : CreateOpenIddictScopeDto
{
}
