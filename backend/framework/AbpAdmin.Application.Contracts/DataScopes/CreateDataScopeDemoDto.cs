using System;
using System.ComponentModel.DataAnnotations;

namespace AbpAdmin.DataScopes;

public class CreateDataScopeDemoDto
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = default!;

    /// <summary>
    /// 显式指定组织单元。为 null 时由写入侧拦截器从当前数据范围快照自动填充。
    /// </summary>
    public Guid? OrganizationUnitId { get; set; }
}
