using System;
using System.ComponentModel.DataAnnotations;

namespace AbpAdmin.Biz.Template.Services.Dtos;

public class CreateBizDataScopeDemoDto
{
    [Required]
    [StringLength(128)]
    public string Name { get; set; } = default!;

    /// <summary>
    /// 显式指定所属组织；留空时由数据范围筛选器的写入侧自动填充（当前快照第一个可见组织；
    /// 算不出组织抛业务异常）。显式指定时服务端校验其落在当前数据范围内（All 范围校验存在性）——
    /// 客户端提交值不可信。组织下拉走框架组织单元接口。
    /// </summary>
    public Guid? OrganizationUnitId { get; set; }
}
