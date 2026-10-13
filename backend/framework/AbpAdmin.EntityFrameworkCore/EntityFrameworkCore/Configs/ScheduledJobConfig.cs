using System;
using AbpAdmin.ScheduledJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 定时作业配置与执行历史实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class ScheduledJobConfig : IEntityTypeConfiguration<ScheduledJob>
{
    public void Configure(EntityTypeBuilder<ScheduledJob> b)
    {
        // T3.3：定时作业配置与执行历史。系统级配置，不挂行级数据权限（IHasDataScope）。
        b.ToTable(AbpAdminConsts.DbTablePrefix + "ScheduledJobs", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Name).IsRequired().HasMaxLength(ScheduledJobConsts.MaxNameLength);
        b.Property(x => x.JobType).IsRequired().HasMaxLength(ScheduledJobConsts.MaxJobTypeLength);
        b.Property(x => x.CronExpression).IsRequired().HasMaxLength(ScheduledJobConsts.MaxCronExpressionLength);
        b.Property(x => x.Description).HasMaxLength(ScheduledJobConsts.MaxDescriptionLength);
        b.Property(x => x.LastRunMessage).HasMaxLength(ScheduledJobConsts.MaxMessageLength);
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.IsEnabled });
    }
}

public class ScheduledJobExecutionConfig : IEntityTypeConfiguration<ScheduledJobExecution>
{
    public void Configure(EntityTypeBuilder<ScheduledJobExecution> b)
    {
        b.ToTable(AbpAdminConsts.DbTablePrefix + "ScheduledJobExecutions", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Message).HasMaxLength(ScheduledJobConsts.MaxMessageLength);
        // 查历史按作业倒序；保留策略清理按租户 + 创建时间
        b.HasIndex(x => new { x.ScheduledJobId, x.StartTime }).IsDescending(false, true);
        b.HasIndex(x => new { x.TenantId, x.CreationTime });
    }
}
