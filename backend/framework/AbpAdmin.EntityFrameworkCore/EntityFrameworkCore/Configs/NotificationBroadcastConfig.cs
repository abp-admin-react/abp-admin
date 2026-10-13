using AbpAdmin.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.EntityFrameworkCore.Configs;

/// <summary>
/// 广播通知批次实体映射（自 AbpAdminDbContext.OnModelCreating 拆出，模型零差异搬移）。
/// </summary>
public class NotificationBroadcastConfig : IEntityTypeConfiguration<NotificationBroadcast>
{
    public void Configure(EntityTypeBuilder<NotificationBroadcast> b)
    {
        // T3.5：广播通知批次（系统级配置，不挂行级数据权限）。
        // 迁移单独生成（Added_NotificationBroadcast），与 Added_NotificationService 分开，便于独立回退。
        b.ToTable(AbpAdminConsts.DbTablePrefix + "NotificationBroadcasts", AbpAdminConsts.DbSchema);
        b.ConfigureByConvention();
        b.Property(x => x.TargetType).IsRequired().HasMaxLength(NotificationBroadcastConsts.MaxTargetTypeLength);
        b.Property(x => x.NotificationMethods).IsRequired().HasMaxLength(NotificationBroadcastConsts.MaxNotificationMethodsLength);
        b.Property(x => x.Title).IsRequired().HasMaxLength(NotificationBroadcastConsts.MaxTitleLength);
        b.Property(x => x.Body).IsRequired();
        b.Property(x => x.State).IsRequired().HasMaxLength(NotificationBroadcastConsts.MaxStateLength);
        b.HasIndex(x => new { x.TenantId, x.CreationTime });
    }
}
