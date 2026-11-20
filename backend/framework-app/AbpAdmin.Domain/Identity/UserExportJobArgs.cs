using System;
using AbpAdmin.Grids;
using Volo.Abp.BackgroundJobs;

namespace AbpAdmin.Identity;

[BackgroundJobName("abpadmin.user-export")]
public class UserExportJobArgs
{
    public Guid? TenantId { get; set; }

    public Guid UserId { get; set; }

    public string Email { get; set; } = default!;

    public string? Filter { get; set; }

    public GridQueryInput? GridQuery { get; set; }

    /// <summary>冻结状态按发起导出的时刻计算，使计数与后台作业使用相同的时间边界。</summary>
    public DateTimeOffset StatusAsOf { get; set; }

    /// <summary>入队用户无 AbpIdentity.Users.Update 权限时为 true：Excel 邮箱/手机号列脱敏输出。</summary>
    public bool MaskSensitive { get; set; } = true;
}
