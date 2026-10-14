using System;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Ledger.Services.Dtos;

public class FinAccountDto : EntityDto<Guid>
{
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public FinAccountType Type { get; set; }
    /// <summary>余额方向（NormalSide）。</summary>
    public FinNormalSide Direction { get; set; }
    public Guid? ParentId { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public bool HasEntries { get; set; }
}

public class FinAccountListInput : PagedAndSortedResultRequestDto
{
    public string? Code { get; set; }
    public string? Name { get; set; }
    public FinAccountType? Type { get; set; }
    public bool? IsActive { get; set; }
}

public class CreateUpdateFinAccountDto
{
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public FinAccountType Type { get; set; }
    public FinNormalSide Direction { get; set; }
    public Guid? ParentId { get; set; }
}
