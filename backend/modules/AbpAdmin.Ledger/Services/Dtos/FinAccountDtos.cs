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

    /// <summary>
    /// 是否启用。默认 null：仅返回启用科目（IsActive=true），避免停用科目出现在科目树/下拉
    /// （如 abp_test2 里已停用的 2203/6601）。显式传 true/false 时按值过滤；管理端「显示停用」传 false。
    /// </summary>
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
