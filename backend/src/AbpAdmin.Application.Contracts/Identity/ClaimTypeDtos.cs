using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

public interface IClaimTypeAppService : IApplicationService
{
    Task<PagedResultDto<ClaimTypeDto>> GetListAsync(PagedAndSortedResultRequestDto input);

    Task<List<ClaimTypeDto>> GetLookupAsync();

    Task<ClaimTypeDto> CreateAsync(CreateClaimTypeDto input);

    Task<ClaimTypeDto> UpdateAsync(Guid id, UpdateClaimTypeDto input);

    Task DeleteAsync(Guid id);
}

public class ClaimTypeDto : EntityDto<Guid>
{
    public string Name { get; set; } = default!;

    public bool Required { get; set; }

    public bool IsStatic { get; set; }

    public string? Regex { get; set; }

    public string? RegexDescription { get; set; }

    public string? Description { get; set; }

    public IdentityClaimValueType ValueType { get; set; }
}

public class CreateClaimTypeDto
{
    // GUI 测试 D4：Name/ValueType 此前无校验——空名与越界枚举值（如 valueType=7）
    // 均可入库。校验与字典标签展示共用同一枚举定义，避免两处漂移。
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = default!;

    public bool Required { get; set; }

    public string? Regex { get; set; }

    public string? RegexDescription { get; set; }

    public string? Description { get; set; }

    // 枚举默认会接受任意整数值（如 7），必须显式校验「值已定义」
    [EnumDataType(typeof(IdentityClaimValueType), ErrorMessage = "未知的值类型。")]
    public IdentityClaimValueType ValueType { get; set; }
}

public class UpdateClaimTypeDto : CreateClaimTypeDto
{
}

public class ClaimValueDto
{
    public string ClaimType { get; set; } = default!;

    public string ClaimValue { get; set; } = default!;
}

public interface IIdentityClaimAppService : IApplicationService
{
    Task<List<ClaimValueDto>> GetUserClaimsAsync(Guid userId);

    Task UpdateUserClaimsAsync(Guid userId, List<ClaimValueDto> claims);

    Task<List<ClaimValueDto>> GetRoleClaimsAsync(Guid roleId);

    Task UpdateRoleClaimsAsync(Guid roleId, List<ClaimValueDto> claims);
}
