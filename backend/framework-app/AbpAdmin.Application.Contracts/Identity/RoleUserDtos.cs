using System;
using Volo.Abp.Application.Dtos;

namespace AbpAdmin.Identity;

/// <summary>
/// 角色下的用户（只读）。刻意只带身份识别与状态字段——不含角色列表/OU 等聚合信息，
/// 复杂维护去用户管理页完成。
/// </summary>
public class RoleUserDto : EntityDto<Guid>
{
    public string UserName { get; set; } = default!;

    public string? Name { get; set; }

    public string? Email { get; set; }

    public string? PhoneNumber { get; set; }

    /// <summary>账号启用状态。禁用用户的角色仍在，但无法登录。</summary>
    public bool IsActive { get; set; }

    /// <summary>锁定截止时间（null = 未锁定）。列表上区分「有角色但被锁」与正常成员。</summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    public DateTime CreationTime { get; set; }
}
