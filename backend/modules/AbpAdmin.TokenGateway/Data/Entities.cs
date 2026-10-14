namespace AbpAdmin.TokenGateway.Data;

/// <summary>钱包计数器。恒等式：可用 = TotalCredited − TotalSettled − Frozen（由流水可重算）。</summary>
public class TgWallet
{
    public Guid UserId { get; set; }
    public long TotalCredited { get; set; }
    public long TotalSettled { get; set; }
    public long Frozen { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public enum LedgerKind { Credit = 1, Reserve = 2, Settle = 3, Release = 4 }

/// <summary>代币流水。(Kind, Ref) 唯一：充值按订单号幂等，预占/结算/解冻按预占单号各一条。</summary>
public class TgLedgerEntry
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public LedgerKind Kind { get; set; }
    public long Amount { get; set; }
    public string Ref { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public enum ReservationStatus { Open = 0, Settled = 1, Released = 2 }

public class TgReservation
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public long Amount { get; set; }
    public long Charged { get; set; }
    public ReservationStatus Status { get; set; }
    public string Alias { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

public class TgApiKey
{
    public Guid UserId { get; set; }
    public string KeyHash { get; set; } = "";
    /// <summary>加密后的 hb- 明文，用于登录复用同一把 Key。</summary>
    public string KeyCipher { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public class TgSession
{
    public string TokenHash { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class TgOrder
{
    public string OrderId { get; set; } = "";
    public Guid UserId { get; set; }
    public string PlanId { get; set; } = "";
    public decimal AmountCny { get; set; }
    public long Tokens { get; set; }
    public Core.PayMethod Method { get; set; }
    public Core.OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int QrVersion { get; set; } = 1;
    public DateTime? PaidAt { get; set; }
}

public class TgUsage
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTime Time { get; set; }
    public string Alias { get; set; } = "";
    public string? Bot { get; set; }
    public long PromptTokens { get; set; }
    public long CompletionTokens { get; set; }
    public long Cost { get; set; }
    public bool Success { get; set; }
}

public class TgPlan
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public decimal PriceCny { get; set; }
    public long Tokens { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime UpdatedAt { get; set; }
}
