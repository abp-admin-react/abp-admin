using System;

namespace AbpAdmin.Biz.Template.Client.Dtos;

/// <summary>客户入口 DTO，独立于管理端 <c>BizProjectDto</c>，不带审计字段。</summary>
public class BizProjectClientDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }
}
