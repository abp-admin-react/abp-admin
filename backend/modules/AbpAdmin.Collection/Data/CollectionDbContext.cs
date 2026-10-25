using AbpAdmin.Collection.Entities;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Modeling;

namespace AbpAdmin.Collection.Data;

/// <summary>
/// 收款模块 DbContext：与宿主同库（Default 连接串）但互不相干，迁移记账在
/// <see cref="CollectionConsts.SchemaHistoryTable"/>（__AbpAdminCollection_EFMigrationsHistory）。
/// 连接串名必须是 Default：ABP 在同一个工作单元里让本上下文与订单上下文共用一个数据库事务
/// （扣款 + 标支付一起提交/回滚的前提，架构测试钉住）。
/// 不复制数据范围筛选器：表没有租户列，只给 Host（计划第 2 节）。
/// </summary>
[ConnectionStringName(CollectionConsts.ConnectionStringName)]
public class CollectionDbContext : AbpDbContext<CollectionDbContext>
{
    public DbSet<ColCollection> Collections { get; set; } = default!;

    public DbSet<ColBalance> Balances { get; set; } = default!;

    public DbSet<ColBalanceTxn> BalanceTxns { get; set; } = default!;

    public DbSet<ColNetwork> Networks { get; set; } = default!;

    public DbSet<ColToken> Tokens { get; set; } = default!;

    public DbSet<ColDepositAddress> DepositAddresses { get; set; } = default!;

    public DbSet<ColDepositOrder> DepositOrders { get; set; } = default!;

    public DbSet<ColSweepTask> SweepTasks { get; set; } = default!;

    public DbSet<ColVaultCheck> VaultChecks { get; set; } = default!;

    public DbSet<ColChainApiKey> ChainApiKeys { get; set; } = default!;

    public DbSet<ColWithdrawOrder> WithdrawOrders { get; set; } = default!;

    public DbSet<ColHotWalletTxn> HotWalletTxns { get; set; } = default!;

    public DbSet<ColHotWalletWatchState> HotWalletWatchStates { get; set; } = default!;

    public CollectionDbContext(DbContextOptions<CollectionDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ColCollection>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "Collections");
            b.ConfigureByConvention();

            b.Property(x => x.CollectionNo).IsRequired().HasMaxLength(CollectionConsts.MaxCollectionNoLength);
            b.Property(x => x.BusinessType).IsRequired().HasMaxLength(CollectionConsts.MaxBusinessTypeLength);
            b.Property(x => x.ChannelTradeNo).HasMaxLength(CollectionConsts.MaxTradeNoLength);
            b.Property(x => x.ChannelPayload).HasMaxLength(CollectionConsts.MaxPayloadLength);
            b.Property(x => x.StatusNote).HasMaxLength(CollectionConsts.MaxNoteLength);
            b.Property(x => x.Amount).HasPrecision(28, 18);

            b.HasIndex(x => x.CollectionNo).IsUnique();

            // 同一业务单只允许一笔 Pending（部分唯一索引；Status 枚举 Pending = 0）。
            // 冲突目标必须带谓词（INSERT ... ON CONFLICT (…) WHERE "Status" = 0 DO NOTHING，
            // 见 CollectionIntegrationService 与计划第 5.1 节）。
            b.HasIndex(x => new { x.BusinessType, x.BusinessId })
                .IsUnique()
                .HasFilter("\"Status\" = 0");

            // 按业务单号列全部收款的查询索引（ListByBusinessAsync 无 Status 谓词，用不了上面的
            // 部分索引——PostgreSQL 只在查询谓词蕴含索引谓词时才选部分索引；补状态作业每轮
            // 按订单逐个调用，没有它就是顺序扫描，评审 H 级）。
            // 显式命名让 EF 把它与同列组的部分唯一索引当成两个索引实体（不命名会被合并吞掉）
            b.HasIndex(x => new { x.BusinessType, x.BusinessId }, "IX_ColCollections_BusinessType_BusinessId_Query");
        });

        builder.Entity<ColBalance>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "Balances");
            b.ConfigureByConvention();

            b.Property(x => x.Currency);
            b.Property(x => x.Balance).HasPrecision(28, 18);
            b.Property(x => x.Frozen).HasPrecision(28, 18);
            b.Property(x => x.UpdatedAt);

            // 唯一 (UserId, Currency)；非负约束由数据库兜底（「余额足够才减」的 SQL 是第一道）
            b.HasIndex(x => new { x.UserId, x.Currency }).IsUnique();
            b.ToTable(t => t.HasCheckConstraint("CK_ColBalances_NonNegative", "\"Balance\" >= 0"));
            // 提现冻结桶非负（Freeze/Unfreeze/DeductFrozen 的条件 UPDATE 是第一道）
            b.ToTable(t => t.HasCheckConstraint("CK_ColBalances_FrozenNonNegative", "\"Frozen\" >= 0"));
        });

        builder.Entity<ColBalanceTxn>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "BalanceTxns");
            b.ConfigureByConvention();

            b.Property(x => x.Seq).UseIdentityColumn();
            b.Property(x => x.Action).IsRequired().HasMaxLength(32);
            b.Property(x => x.Amount).HasPrecision(28, 18);
            b.Property(x => x.BalanceAfter).HasPrecision(28, 18);
            b.Property(x => x.IdempotencyKey).IsRequired().HasMaxLength(128);
            b.Property(x => x.BusinessType).HasMaxLength(CollectionConsts.MaxBusinessTypeLength);

            b.HasIndex(x => x.IdempotencyKey).IsUnique();
            b.HasIndex(x => new { x.UserId, x.Currency });

            // 用户流水分页（GetMyTxnsAsync 按 Seq 倒序 Skip/Take）：没有它每次请求
            // 都读全量再排序，活跃用户几个月就到万级行（评审 M 级）
            b.HasIndex(x => new { x.UserId, x.Seq }, "IX_ColBalanceTxns_UserId_Seq");

            // 「是否已有未回冲扣款」由部分唯一索引判断（计划第 5.5 节），不把业务单号本身
            // 当作 IdempotencyKey：回冲后再次扣款要能插进新行。
            // 扣款行的 ON CONFLICT 目标必须带谓词，与 ColCollections 的 Pending 索引同一纪律。
            b.HasIndex(x => new { x.BusinessType, x.BusinessId })
                .IsUnique()
                .HasFilter("\"Action\" = 'BusinessDebit' AND \"ReversedAt\" IS NULL");

            // ListCreditedTxnsAsync 的正向扫描（Seq 升序 + 只看充值行）
            b.HasIndex(x => new { x.Action, x.Seq }, "IX_ColBalanceTxns_Action_Seq");

            // 7 天重扫按时间倒查充值行
            b.HasIndex(x => new { x.Action, x.CreationTime }, "IX_ColBalanceTxns_Action_CreationTime");
        });

        builder.Entity<ColNetwork>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "Networks");
            b.ConfigureByConvention();

            b.Property(x => x.Name).IsRequired().HasMaxLength(32);
            b.Property(x => x.MnemonicCipher);
            b.Property(x => x.RpcCipher);

            b.HasIndex(x => x.Name).IsUnique();
        });

        builder.Entity<ColToken>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "Tokens");
            b.ConfigureByConvention();

            b.Property(x => x.Symbol).IsRequired().HasMaxLength(16);
            b.Property(x => x.ContractAddress).IsRequired().HasMaxLength(42);
            b.Property(x => x.MinDeposit).IsRequired().HasMaxLength(32);
            b.Property(x => x.VaultAddress).HasMaxLength(42);

            // 一网一币一符号；扫链游标与合约都在行上
            b.HasIndex(x => new { x.NetworkId, x.Symbol }).IsUnique();
            b.HasIndex(x => new { x.NetworkId, x.ContractAddress }).IsUnique();
        });

        builder.Entity<ColDepositAddress>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "DepositAddresses");
            b.ConfigureByConvention();

            b.Property(x => x.Address).IsRequired().HasMaxLength(42);

            // 计划第 5.6 节：唯一 (UserId, NetworkId)、唯一 (NetworkId, AddressIndex)——
            // 并发派生撞索引靠后者兜底（锁内重读）
            b.HasIndex(x => new { x.UserId, x.NetworkId }).IsUnique();
            b.HasIndex(x => new { x.NetworkId, x.AddressIndex }).IsUnique();
            b.HasIndex(x => new { x.NetworkId, x.Address });
        });

        builder.Entity<ColDepositOrder>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "DepositOrders");
            b.ConfigureByConvention();

            b.Property(x => x.TxHash).IsRequired().HasMaxLength(66);
            b.Property(x => x.ToAddress).IsRequired().HasMaxLength(42);
            b.Property(x => x.FromAddress).IsRequired().HasMaxLength(42);
            b.Property(x => x.RawAmount).IsRequired().HasMaxLength(96);
            b.Property(x => x.Amount).HasPrecision(28, 18);
            b.Property(x => x.Fee).HasPrecision(28, 18);
            b.Property(x => x.ActualAmount).HasPrecision(28, 18);
            b.Property(x => x.FeeRateSnapshot).HasPrecision(18, 8);

            // 计划第 5.6 节：唯一 (TxHash, LogIndex)——重启不重复建单
            b.HasIndex(x => new { x.TxHash, x.LogIndex }).IsUnique();
            b.HasIndex(x => new { x.Status, x.BlockNumber });
            b.HasIndex(x => new { x.TokenId, x.Status });
            b.HasIndex(x => x.TxHash);

            // 跳过比例窗口聚合（TokenId + 时间）与用户进款分页（UserId + 时间）——
            // 表单调增长，无索引则每轮/每请求全扫（评审 M 级）
            b.HasIndex(x => new { x.TokenId, x.CreationTime });
            b.HasIndex(x => new { x.UserId, x.CreationTime });
        });

        builder.Entity<ColSweepTask>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "SweepTasks");
            b.ConfigureByConvention();

            b.Property(x => x.Address).IsRequired().HasMaxLength(42);
            b.Property(x => x.Amount).HasPrecision(28, 18);
            b.Property(x => x.TxHash).HasMaxLength(66);
            b.Property(x => x.GasTxHash).HasMaxLength(66);
            b.Property(x => x.FailReason).HasMaxLength(CollectionConsts.MaxNoteLength);

            // 同址同币同时最多一个未完成任务（Queued 的可合并；这里锁的是「激活后」的独占，
            // 与地址级互斥——同地址已有 Fueling/Sweeping 时另一币不能再占——两条部分唯一索引）
            b.HasIndex(x => new { x.AddressId, x.TokenId })
                .IsUnique()
                .HasFilter("\"Status\" IN (1, 2)");
            b.HasIndex(x => new { x.AddressId })
                .IsUnique()
                .HasFilter("\"Status\" IN (1, 2)");
            b.HasIndex(x => new { x.Status, x.StatusChangedAt });
        });

        builder.Entity<ColVaultCheck>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "VaultChecks");
            b.ConfigureByConvention();

            b.Property(x => x.Symbol).IsRequired().HasMaxLength(16);
            b.Property(x => x.RawVaultAmount).IsRequired().HasMaxLength(96);
            b.Property(x => x.VaultBalance).HasPrecision(28, 18);
            b.Property(x => x.UserTotal).HasPrecision(28, 18);
            b.Property(x => x.Difference).HasPrecision(28, 18);

            b.HasIndex(x => new { x.TokenId, x.CheckedAt });
        });

        builder.Entity<ColChainApiKey>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "ChainApiKeys");
            b.ConfigureByConvention();

            b.Property(x => x.Label).HasMaxLength(64);
            b.Property(x => x.KeyHash).IsRequired().HasMaxLength(64);

            // 同一把 key（同网络同服务商）不重复入库——判重靠 KeyHash（密文带随机 IV 不可比）
            b.HasIndex(x => new { x.NetworkId, x.Provider, x.KeyHash }).IsUnique();
            // 工厂取启用 key 的查询路径
            b.HasIndex(x => new { x.NetworkId, x.Provider, x.IsEnabled });
        });

        builder.Entity<ColWithdrawOrder>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "WithdrawOrders");
            b.ConfigureByConvention();

            b.Property(x => x.NetworkName).IsRequired().HasMaxLength(32);
            b.Property(x => x.ToAddress).IsRequired().HasMaxLength(42);
            b.Property(x => x.Amount).HasPrecision(28, 18);
            b.Property(x => x.Fee).HasPrecision(28, 18);
            b.Property(x => x.ActualAmount).HasPrecision(28, 18);
            b.Property(x => x.FeeRate).HasPrecision(18, 8);
            b.Property(x => x.TxHash).HasMaxLength(66);
            b.Property(x => x.FailReason).HasMaxLength(CollectionConsts.MaxNoteLength);
            b.Property(x => x.AuditRemark).HasMaxLength(CollectionConsts.MaxNoteLength);

            // 用户侧「我的提现」分页 + 当日额度聚合（SubmitAsync 锁内计数）
            b.HasIndex(x => new { x.UserId, x.CreationTime });
            // 广播 worker 的取件路径（Processing 队列 / Broadcasting 超时扫描）
            b.HasIndex(x => new { x.Status, x.StatusChangedAt });
            // 双花门：近期是否已向该地址付过款
            b.HasIndex(x => new { x.ToAddress, x.CompleteTime });
            // 热钱包监控按 TxHash 批量对账（部分索引：广播前 NULL 行不进索引；否则每轮每币全表扫）
            b.HasIndex(x => x.TxHash).HasFilter("\"TxHash\" IS NOT NULL");
            // 资金看板「今日」窗口（CreationTime 打头；复合索引 (UserId,CreationTime) 前导列不符用不上）
            b.HasIndex(x => x.CreationTime);
        });

        builder.Entity<ColHotWalletTxn>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "HotWalletTxns");
            b.ConfigureByConvention();

            b.Property(x => x.Symbol).IsRequired().HasMaxLength(16);
            b.Property(x => x.TxHash).IsRequired().HasMaxLength(66);
            b.Property(x => x.From).IsRequired().HasMaxLength(42);
            b.Property(x => x.To).IsRequired().HasMaxLength(42);
            b.Property(x => x.RawAmount).IsRequired().HasMaxLength(96);
            b.Property(x => x.Amount).HasPrecision(28, 18);
            b.Property(x => x.AckedBy).HasMaxLength(64);
            b.Property(x => x.AckNote).HasMaxLength(CollectionConsts.MaxNoteLength);

            // 重扫幂等：同一 Transfer 日志只落一行（tokentx 分页重合/重启重扫由它吸收）
            b.HasIndex(x => new { x.TxHash, x.LogIndex }).IsUnique();
            // 管理端流水分页（默认最新在前：块高序与落库时间同向）
            b.HasIndex(x => new { x.BlockNumber, x.LogIndex });
            // 资金看板「今日」窗口（同提现表）
            b.HasIndex(x => x.CreationTime);
            // 管理端流水分页（默认最新在前）与异常筛选
            b.HasIndex(x => x.CreationTime);
            b.HasIndex(x => new { x.Status, x.CreationTime });
        });

        builder.Entity<ColHotWalletWatchState>(b =>
        {
            b.ToTable(CollectionConsts.DbTablePrefix + "HotWalletWatchStates");
            b.ConfigureByConvention();

            b.Property(x => x.Symbol).IsRequired().HasMaxLength(16);
            b.Property(x => x.LastError).HasMaxLength(CollectionConsts.MaxNoteLength);

            b.HasIndex(x => x.TokenId).IsUnique();
        });
    }
}
