using AbpAdmin.OperationLogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 操作日志实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，与其他 Config 同款）。
/// </summary>
public class OperationLogConfig : IEntityTypeConfiguration<OperationLog>
{
    public void Configure(EntityTypeBuilder<OperationLog> b)
    {
        // 追加-only 日志表：无外键（用户为快照字段），查询按时间/用户过滤。
        b.ToTable(AbpAdminConsts.DbTablePrefix + "OperationLogs", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.Type).IsRequired().HasMaxLength(OperationLogConsts.MaxTypeLength);
        b.Property(x => x.SubType).IsRequired().HasMaxLength(OperationLogConsts.MaxSubTypeLength);
        b.Property(x => x.Action).HasMaxLength(OperationLogConsts.MaxActionLength);
        b.Property(x => x.Extra).HasMaxLength(OperationLogConsts.MaxExtraLength);
        b.Property(x => x.CorrelationId).HasMaxLength(OperationLogConsts.MaxCorrelationIdLength);
        b.HasIndex(x => new { x.TenantId, x.ExecutionTime });
        b.HasIndex(x => new { x.TenantId, x.UserId });
        // 跨日志串联入口（按 CorrelationId 精确过滤）：追加型日志表不建索引会随时间退化为全表扫描
        b.HasIndex(x => new { x.TenantId, x.CorrelationId });
    }
}
