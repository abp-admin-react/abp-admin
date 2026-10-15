using Microsoft.EntityFrameworkCore;

namespace AbpAdmin.TokenGateway.Data;

/// <summary>
/// 纯 EF DbContext（不走 ABP UoW）：钱包操作需要显式事务 + 条件 UPDATE 保证原子，
/// 自己管事务比套 UoW 更直白。迁移独立 History 表 __TokenGateway_EFMigrationsHistory。
/// </summary>
public class TokenGatewayDbContext(DbContextOptions<TokenGatewayDbContext> options) : DbContext(options)
{
    private const string P = TokenGatewayConsts.DbTablePrefix;

    public DbSet<TgWallet> Wallets => Set<TgWallet>();
    public DbSet<TgLedgerEntry> Ledger => Set<TgLedgerEntry>();
    public DbSet<TgReservation> Reservations => Set<TgReservation>();
    public DbSet<TgApiKey> ApiKeys => Set<TgApiKey>();
    public DbSet<TgSession> Sessions => Set<TgSession>();
    public DbSet<TgOrder> Orders => Set<TgOrder>();
    public DbSet<TgUsage> Usage => Set<TgUsage>();
    public DbSet<TgPlan> Plans => Set<TgPlan>();

    public static void ConfigureNpgsql(DbContextOptionsBuilder b, string connectionString) =>
        b.UseNpgsql(connectionString, n => n.MigrationsHistoryTable(TokenGatewayConsts.SchemaHistoryTable));

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TgWallet>(e =>
        {
            e.ToTable(P + "Wallets");
            e.HasKey(x => x.UserId);
        });
        b.Entity<TgLedgerEntry>(e =>
        {
            e.ToTable(P + "LedgerEntries");
            e.Property(x => x.Ref).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.Kind, x.Ref }).IsUnique();
            // (UserId, Kind, CreatedAt) + INCLUDE(Amount)：ViewAsync 的当月 PeriodUsed 聚合走
            // index-only 扫描——旧 (UserId, CreatedAt) 要回表过滤 Kind，重度用户当月流水上万行时
            // 每次余额查询都重扫（OCR performance 修复）
            e.HasIndex(x => new { x.UserId, x.Kind, x.CreatedAt }).IncludeProperties(x => x.Amount);
        });
        b.Entity<TgReservation>(e =>
        {
            e.ToTable(P + "Reservations");
            e.Property(x => x.Alias).HasMaxLength(64);
            e.HasIndex(x => new { x.UserId, x.Status });
        });
        b.Entity<TgApiKey>(e =>
        {
            e.ToTable(P + "ApiKeys");
            e.HasKey(x => x.UserId);
            e.Property(x => x.KeyHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.KeyCipher).HasMaxLength(512).IsRequired();
            e.HasIndex(x => x.KeyHash).IsUnique();
        });
        b.Entity<TgSession>(e =>
        {
            e.ToTable(P + "Sessions");
            e.HasKey(x => x.TokenHash);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.ExpiresAt);
        });
        b.Entity<TgOrder>(e =>
        {
            e.ToTable(P + "Orders");
            e.HasKey(x => x.OrderId);
            e.Property(x => x.OrderId).HasMaxLength(40);
            e.Property(x => x.PlanId).HasMaxLength(32);
            e.Property(x => x.AmountCny).HasPrecision(18, 2);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });
        b.Entity<TgUsage>(e =>
        {
            e.ToTable(P + "Usage");
            e.Property(x => x.Alias).HasMaxLength(64);
            e.Property(x => x.Bot).HasMaxLength(128);
            e.HasIndex(x => new { x.UserId, x.Time });
        });
        b.Entity<TgPlan>(e =>
        {
            e.ToTable(P + "Plans");
            e.Property(x => x.Id).HasMaxLength(32);
            e.Property(x => x.Name).HasMaxLength(64).IsRequired();
            e.Property(x => x.PriceCny).HasPrecision(18, 2);
            // 种子四档，占位口径 ¥1 = 100,000 代币，无加赠；改代币数走管理接口，不发版。
            var at = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
            e.HasData(
                new TgPlan { Id = "p30", Name = "体验包", PriceCny = 30, Tokens = 3_000_000, SortOrder = 1, IsActive = true, UpdatedAt = at },
                new TgPlan { Id = "p98", Name = "标准包", PriceCny = 98, Tokens = 9_800_000, SortOrder = 2, IsActive = true, UpdatedAt = at },
                new TgPlan { Id = "p198", Name = "进阶包", PriceCny = 198, Tokens = 19_800_000, SortOrder = 3, IsActive = true, UpdatedAt = at },
                new TgPlan { Id = "p498", Name = "专业包", PriceCny = 498, Tokens = 49_800_000, SortOrder = 4, IsActive = true, UpdatedAt = at });
        });
    }
}
