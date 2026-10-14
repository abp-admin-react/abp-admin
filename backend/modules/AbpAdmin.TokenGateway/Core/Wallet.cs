namespace AbpAdmin.TokenGateway.Core;

public record BalanceView(int Percent, bool Low, long Balance, long Frozen, long Available, long TotalCredited, long PeriodUsed, DateTimeOffset UpdatedAt);

/// <summary>
/// 代币账本。恒等式：Balance = Σ充值 − Σ已结算；Available = Balance − Frozen。
/// 单进程内按用户加锁保证 reserve 互斥；多实例/落库后改行锁。
/// </summary>
public class Wallet(TimeProvider clock)
{
    private sealed class Account
    {
        public long Balance, Frozen, TotalCredited;
        public readonly Dictionary<Guid, long> Reservations = new();
        public readonly HashSet<string> CreditRefs = new();
        public readonly List<(DateTimeOffset At, long Cost)> Settled = new();
        public DateTimeOffset UpdatedAt;
    }

    private readonly Dictionary<Guid, Account> _accounts = new();

    private Account Get(Guid user)
    {
        lock (_accounts)
        {
            if (!_accounts.TryGetValue(user, out var a)) _accounts[user] = a = new Account { UpdatedAt = clock.GetUtcNow() };
            return a;
        }
    }

    /// <summary>充值入账，按 creditRef（订单号）幂等。</summary>
    public bool Credit(Guid user, long tokens, string creditRef)
    {
        if (tokens <= 0) throw new ArgumentOutOfRangeException(nameof(tokens));
        var a = Get(user);
        lock (a)
        {
            if (!a.CreditRefs.Add(creditRef)) return false;
            a.Balance += tokens;
            a.TotalCredited += tokens;
            a.UpdatedAt = clock.GetUtcNow();
            return true;
        }
    }

    /// <summary>可用不足直接拒（断供），不允许负余额。</summary>
    public Guid Reserve(Guid user, long amount)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var a = Get(user);
        lock (a)
        {
            if (a.Balance - a.Frozen < amount) throw GatewayException.InsufficientBalance();
            var id = Guid.NewGuid();
            a.Reservations[id] = amount;
            a.Frozen += amount;
            a.UpdatedAt = clock.GetUtcNow();
            return id;
        }
    }

    /// <summary>按实际用量结算，最多扣到预占额；差额解冻。返回实扣。</summary>
    public long Settle(Guid user, Guid reservation, long actual)
    {
        var a = Get(user);
        lock (a)
        {
            if (!a.Reservations.Remove(reservation, out var held)) return 0;
            var charge = Math.Clamp(actual, 0, held);
            a.Frozen -= held;
            a.Balance -= charge;
            a.Settled.Add((clock.GetUtcNow(), charge));
            a.UpdatedAt = clock.GetUtcNow();
            return charge;
        }
    }

    public void Release(Guid user, Guid reservation) => Settle(user, reservation, 0);

    public BalanceView View(Guid user)
    {
        var a = Get(user);
        lock (a)
        {
            var available = a.Balance - a.Frozen;
            var percent = a.TotalCredited == 0 ? 0 : (int)Math.Floor(available * 100m / a.TotalCredited);
            var periodStart = PeriodStart(clock.GetUtcNow());
            var used = a.Settled.Where(s => s.At >= periodStart).Sum(s => s.Cost);
            return new BalanceView(percent, percent < 10, a.Balance, a.Frozen, available, a.TotalCredited, used, a.UpdatedAt);
        }
    }

    /// <summary>本期 = 北京时间自然月。</summary>
    internal static DateTimeOffset PeriodStart(DateTimeOffset nowUtc)
    {
        var cn = nowUtc.ToOffset(TimeSpan.FromHours(8));
        return new DateTimeOffset(cn.Year, cn.Month, 1, 0, 0, 0, TimeSpan.FromHours(8));
    }
}
