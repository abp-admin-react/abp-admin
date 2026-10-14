namespace AbpAdmin.Ledger.Integration.Contracts;

/// <summary>事件里的账户引用（解析成子账户由 IAccountResolver 完成，F2）。</summary>
public sealed record AccountRef(
    string AccountCode,
    FinOwnerType? OwnerType = null,
    string? OwnerId = null,
    string? Bucket = null);
