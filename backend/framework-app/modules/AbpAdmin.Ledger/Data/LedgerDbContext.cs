using AbpAdmin.Ledger.Entities;
using Microsoft.EntityFrameworkCore;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;

namespace AbpAdmin.Ledger.Data;

/// <summary>
/// Ledger DbContext：同库 Default、独立 History __AbpAdminLedger_EFMigrationsHistory。
/// </summary>
[ConnectionStringName(LedgerConsts.ConnectionStringName)]
public class LedgerDbContext : AbpDbContext<LedgerDbContext>
{
    public DbSet<FinAccount> Accounts { get; set; } = default!;
    public DbSet<FinSubAccount> SubAccounts { get; set; } = default!;
    public DbSet<FinVoucher> Vouchers { get; set; } = default!;
    public DbSet<FinEntry> Entries { get; set; } = default!;
    public DbSet<FinBalanceProjection> BalanceProjections { get; set; } = default!;
    public DbSet<FinPeriod> Periods { get; set; } = default!;
    public DbSet<FinReconcileRun> ReconcileRuns { get; set; } = default!;
    public DbSet<FinManualAdjust> ManualAdjusts { get; set; } = default!;
    public DbSet<FinPostingRule> PostingRules { get; set; } = default!;

    public LedgerDbContext(DbContextOptions<LedgerDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfiguration(new FinAccountConfig());
        builder.ApplyConfiguration(new FinSubAccountConfig());
        builder.ApplyConfiguration(new FinVoucherConfig());
        builder.ApplyConfiguration(new FinEntryConfig());
        builder.ApplyConfiguration(new FinBalanceProjectionConfig());
        builder.ApplyConfiguration(new FinPeriodConfig());
        builder.ApplyConfiguration(new FinReconcileRunConfig());
        builder.ApplyConfiguration(new FinManualAdjustConfig());
        builder.ApplyConfiguration(new FinPostingRuleConfig());
    }
}
