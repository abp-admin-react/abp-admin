namespace AbpAdmin.Ledger;

public static class LedgerConsts
{
    public const string ConnectionStringName = "Default";

    /// <summary>表前缀：FinAccounts / FinVouchers …</summary>
    public const string DbTablePrefix = "Fin";

    public const string? DbSchema = null;

    public const string SchemaHistoryTable = "__AbpAdminLedger_EFMigrationsHistory";

    /// <summary>首个迁移 Id；生成后与 Migrations/ 下 Initial 文件名同步。</summary>
    public const string InitialMigrationId = "20261013230841_Initial";

    public const int MaxCodeLength = 32;
    public const int MaxNameLength = 128;
    public const int MaxBizTypeLength = 64;
    public const int MaxBizRefIdLength = 64;
    public const int MaxPhaseLength = 32;
    public const int MaxBatchKeyLength = 64;
    public const int MaxCurrencyLength = 16;
    public const int MaxSummaryLength = 512;
    public const int MaxBucketLength = 64;
    public const int MaxOwnerIdLength = 64;
}
