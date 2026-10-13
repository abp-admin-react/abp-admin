using AbpAdmin.Webhooks.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Webhooks;

/// <summary>
/// 模块自有 DbContext：与宿主 AbpAdminDbContext 同库（Default 连接串）但互不相干。
/// 迁移记账在 <see cref="AbpAdminWebhooksConsts.SchemaHistoryTable"/>（见模块的
/// 上下文级 UseNpgsql 配置），与框架 __EFMigrationsHistory 两本账。
/// </summary>
[ConnectionStringName(AbpAdminWebhooksConsts.ConnectionStringName)]
public class WebhooksDbContext : AbpDbContext<WebhooksDbContext>
{
    public DbSet<WebhookSubscription> Subscriptions { get; set; } = default!;
    public DbSet<WebhookSubscriptionEvent> SubscriptionEvents { get; set; } = default!;
    public DbSet<WebhookSendRecord> SendRecords { get; set; } = default!;

    public WebhooksDbContext(DbContextOptions<WebhooksDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<WebhookSubscription>(b =>
        {
            b.ToTable(AbpAdminWebhooksConsts.DbTablePrefix + "Subscriptions");
            b.ConfigureByConvention();

            b.Property(x => x.WebhookUri).IsRequired().HasMaxLength(WebhooksConsts.MaxWebhookUriLength);
            b.Property(x => x.Secret).IsRequired().HasMaxLength(WebhooksConsts.MaxSecretLength);
            b.Property(x => x.Description).HasMaxLength(WebhooksConsts.MaxDescriptionLength);

            // 发布路径按"活跃订阅"过滤 + 租户维度——复合索引
            b.HasIndex(x => new { x.TenantId, x.IsActive });

            b.HasMany(x => x.Events)
                .WithOne()
                .HasForeignKey(e => e.SubscriptionId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WebhookSubscriptionEvent>(b =>
        {
            b.ToTable(AbpAdminWebhooksConsts.DbTablePrefix + "SubscriptionEvents");
            b.ConfigureByConvention();

            // 复合主键（见实体注释）：聚合差量增删不需要代理 Guid
            b.HasKey(e => new { e.SubscriptionId, e.EventName });

            b.Property(x => x.EventName).IsRequired().HasMaxLength(WebhooksConsts.MaxEventNameLength);

            // 发布路径的索引：按事件名找活跃订阅（与订阅 IsActive join）
            b.HasIndex(x => x.EventName);
        });

        builder.Entity<WebhookSendRecord>(b =>
        {
            b.ToTable(AbpAdminWebhooksConsts.DbTablePrefix + "SendRecords");
            b.ConfigureByConvention();

            b.Property(x => x.EventName).IsRequired().HasMaxLength(WebhooksConsts.MaxEventNameLength);
            b.Property(x => x.Payload).IsRequired().HasMaxLength(WebhooksConsts.MaxPayloadLength);
            b.Property(x => x.ResponseBody).HasMaxLength(AbpAdminWebhooksConsts.MaxResponseBodyLength);

            // 记录列表的常规视图：订阅维度倒序 + 失败排查看 Succeeded
            b.HasIndex(x => new { x.SubscriptionId, x.CreationTime });
            b.HasIndex(x => x.Succeeded);
        });
    }
}
