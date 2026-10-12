using AbpAdmin.Ordering.Entities;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Ordering.Data;

/// <summary>
/// 订单模块 DbContext：与宿主同库（Default 连接串）、迁移记账在
/// <see cref="OrderingConsts.SchemaHistoryTable"/>（__AbpAdminOrdering_EFMigrationsHistory）。
/// 连接串名必须是 Default：扣款和把订单标成 Paid 必须放在同一个工作单元里（计划第 2 节）。
/// 不复制数据范围筛选器：表没有租户列，只给 Host。
/// </summary>
[ConnectionStringName(OrderingConsts.ConnectionStringName)]
public class OrderingDbContext : AbpDbContext<OrderingDbContext>
{
    public DbSet<OrdOrder> Orders { get; set; } = default!;

    public DbSet<OrdQuote> Quotes { get; set; } = default!;

    public DbSet<OrdPaymentAnomalyLine> PaymentAnomalyLines { get; set; } = default!;

    public DbSet<OrdJobState> JobStates { get; set; } = default!;

    public OrderingDbContext(DbContextOptions<OrderingDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<OrdOrder>(b =>
        {
            b.ToTable(OrderingConsts.DbTablePrefix + "Orders");
            b.ConfigureByConvention();

            b.Property(x => x.OrderNo).IsRequired().HasMaxLength(OrderingConsts.MaxOrderNoLength);
            b.Property(x => x.Subject).IsRequired().HasMaxLength(OrderingConsts.MaxSubjectLength);
            b.Property(x => x.Currency).HasMaxLength(8);
            b.Property(x => x.Amount).HasPrecision(18, 2);
            b.Property(x => x.PayAmount).HasPrecision(28, 18);
            b.Property(x => x.ChannelTradeNo).HasMaxLength(OrderingConsts.MaxTradeNoLength);
            b.Property(x => x.RefundedAmount).HasPrecision(18, 2);
            b.Property(x => x.ClosedAt);

            b.HasIndex(x => x.OrderNo).IsUnique();
            b.HasIndex(x => x.UserId);

            // 用户/管理端列表的默认排序（CreationTime 倒序分页）：订单只增不删，
            // 没有它每次列表都是全表 top-N 排序（评审遗留核实为真问题）
            b.HasIndex(x => x.CreationTime);

            // 补状态作业的扫描形状（OrderStatusSyncJobHandler）：Status 单列索引基数太低
            //（充值单永远 Paid、关闭单永远 Closed，候选集随表无限增长），按扫描谓词建部分索引
            b.HasIndex(x => x.Status);
            b.HasIndex(x => x.PaidAt)
                .HasFilter("\"Status\" = 1 AND \"OrderType\" = 2");   // Paid 扫描（回看 7 天）
            b.HasIndex(x => x.ExpiresAt)
                .HasFilter("\"Status\" = 2 AND \"OrderType\" = 2");   // Closed 扫描（回看 7 天；Closed = 2）

            // 充值订单的 BalanceTxnId 唯一（业务单也用该列做致付归因，所以是部分唯一索引）
            b.HasIndex(x => x.BalanceTxnId)
                .IsUnique()
                .HasFilter("\"OrderType\" = 1");
        });

        builder.Entity<OrdQuote>(b =>
        {
            b.ToTable(OrderingConsts.DbTablePrefix + "Quotes");
            b.ConfigureByConvention();

            b.Property(x => x.PayAmount).HasPrecision(28, 18);
            b.Property(x => x.UsdCnyRate).HasPrecision(18, 8);

            // 同一订单、同一支付方式、Active 只有一行（QuoteStatus.Active = 1）。
            // PostgreSQL 部分索引条件里不能写 now()——过期靠插入时翻状态（计划第 4.2 节）。
            b.HasIndex(x => new { x.OrderId, x.PayMethod })
                .IsUnique()
                .HasFilter("\"Status\" = 1");
        });

        builder.Entity<OrdPaymentAnomalyLine>(b =>
        {
            b.ToTable(OrderingConsts.DbTablePrefix + "PaymentAnomalyLines");
            b.ConfigureByConvention();

            b.Property(x => x.ChannelTradeNo).HasMaxLength(OrderingConsts.MaxTradeNoLength);
            b.Property(x => x.Note).IsRequired().HasMaxLength(OrderingConsts.MaxNoteLength);
            b.Property(x => x.Amount).HasPrecision(28, 18);

            b.HasIndex(x => x.OrderId);
        });

        builder.Entity<OrdJobState>(b =>
        {
            b.ToTable(OrderingConsts.DbTablePrefix + "JobStates");
            b.ConfigureByConvention();

            b.Property(x => x.Id).HasMaxLength(128);
            b.Property(x => x.SkippedTxnsJson);
        });
    }
}
