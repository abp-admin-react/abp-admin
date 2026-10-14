using AbpAdmin.TokenGateway.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AbpAdmin.TokenGateway.Core;

public record BalanceView(int Percent, bool Low, long Balance, long Frozen, long Available, long TotalCredited, long PeriodUsed, DateTimeOffset UpdatedAt);

public record ReconcileResult(bool Ok, long WalletCredited, long LedgerCredited, long WalletSettled, long LedgerSettled, long WalletFrozen, long OpenReservations);

/// <summary>
/// 代币账本（落库）。可用 = 累计充值 − 累计已结算 − 当前冻结；Balance（对外字段）= 累计充值 − 累计已结算。
/// 原子性：钱包计数器只用条件 UPDATE 改（PG 行锁 + WHERE 重评估），预占不足即 0 行 → 断供；
/// 预占单状态也用条件 UPDATE 翻转，保证每单只结算/解冻一次。每步都写流水，同一事务提交。
/// </summary>
public class Wallet(IDbContextFactory<TokenGatewayDbContext> dbf, TimeProvider clock)
{
    private DateTime Now() => clock.GetUtcNow().UtcDateTime;

    /// <summary>充值入账，按 creditRef 幂等（流水 (Kind,Ref) 唯一）。返回是否本次入账。</summary>
    public async Task<bool> CreditAsync(Guid user, long tokens, string creditRef)
    {
        await using var db = await dbf.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        if (!await CreditInTxAsync(db, user, tokens, creditRef)) return false;
        await tx.CommitAsync();
        return true;
    }

    internal async Task<bool> CreditInTxAsync(TokenGatewayDbContext db, Guid user, long tokens, string creditRef)
    {
        if (tokens <= 0) throw new ArgumentOutOfRangeException(nameof(tokens));
        var now = Now();
        // ON CONFLICT DO NOTHING 不会让事务失败，重复入账得到 0 行。
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TgLedgerEntries" ("Id","UserId","Kind","Amount","Ref","CreatedAt")
            VALUES ({Guid.NewGuid()}, {user}, {(int)LedgerKind.Credit}, {tokens}, {creditRef}, {now})
            ON CONFLICT ("Kind","Ref") DO NOTHING
            """);
        if (inserted == 0) return false;
        await EnsureWalletAsync(db, user, now);
        await db.Wallets.Where(w => w.UserId == user).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.TotalCredited, w => w.TotalCredited + tokens)
            .SetProperty(w => w.UpdatedAt, now));
        return true;
    }

    /// <summary>可用不足直接拒（断供），不允许负余额。</summary>
    public async Task<Guid> ReserveAsync(Guid user, long amount, string alias)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        var now = Now();
        await using var db = await dbf.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        await EnsureWalletAsync(db, user, now);
        var n = await db.Wallets
            .Where(w => w.UserId == user && w.TotalCredited - w.TotalSettled - w.Frozen >= amount)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Frozen, w => w.Frozen + amount).SetProperty(w => w.UpdatedAt, now));
        if (n == 0) throw GatewayException.InsufficientBalance();

        var id = Guid.NewGuid();
        db.Reservations.Add(new TgReservation { Id = id, UserId = user, Amount = amount, Status = ReservationStatus.Open, Alias = alias, CreatedAt = now });
        db.Ledger.Add(new TgLedgerEntry { Id = Guid.NewGuid(), UserId = user, Kind = LedgerKind.Reserve, Amount = amount, Ref = id.ToString("N"), CreatedAt = now });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return id;
    }

    /// <summary>按实际用量结算，最多扣到预占额，差额解冻。重复调用返回 0 不重复扣。</summary>
    public Task<long> SettleAsync(Guid user, Guid reservation, long actual) => CloseAsync(user, reservation, actual, settle: true);

    /// <summary>失败/上游未计费：全额解冻（不是退款）。</summary>
    public Task ReleaseAsync(Guid user, Guid reservation) => CloseAsync(user, reservation, 0, settle: false);

    private async Task<long> CloseAsync(Guid user, Guid reservation, long actual, bool settle)
    {
        var now = Now();
        await using var db = await dbf.CreateDbContextAsync();
        await using var tx = await db.Database.BeginTransactionAsync();
        var held = await db.Reservations.AsNoTracking()
            .Where(r => r.Id == reservation && r.UserId == user).Select(r => (long?)r.Amount).FirstOrDefaultAsync();
        if (held is null) return 0;
        var charge = settle ? Math.Clamp(actual, 0, held.Value) : 0;
        var status = settle ? ReservationStatus.Settled : ReservationStatus.Released;

        var flipped = await db.Reservations
            .Where(r => r.Id == reservation && r.Status == ReservationStatus.Open)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status).SetProperty(r => r.Charged, charge).SetProperty(r => r.ClosedAt, now));
        if (flipped == 0) return 0;

        await db.Wallets.Where(w => w.UserId == user).ExecuteUpdateAsync(s => s
            .SetProperty(w => w.Frozen, w => w.Frozen - held.Value)
            .SetProperty(w => w.TotalSettled, w => w.TotalSettled + charge)
            .SetProperty(w => w.UpdatedAt, now));

        var refId = reservation.ToString("N");
        if (charge > 0)
            db.Ledger.Add(new TgLedgerEntry { Id = Guid.NewGuid(), UserId = user, Kind = LedgerKind.Settle, Amount = charge, Ref = refId, CreatedAt = now });
        if (held.Value - charge > 0)
            db.Ledger.Add(new TgLedgerEntry { Id = Guid.NewGuid(), UserId = user, Kind = LedgerKind.Release, Amount = held.Value - charge, Ref = refId, CreatedAt = now });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return charge;
    }

    public async Task<BalanceView> ViewAsync(Guid user)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var w = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == user);
        var periodStart = PeriodStart(clock.GetUtcNow()).UtcDateTime;
        var used = await db.Ledger.Where(e => e.UserId == user && e.Kind == LedgerKind.Settle && e.CreatedAt >= periodStart).SumAsync(e => (long?)e.Amount) ?? 0;
        if (w is null) return new BalanceView(0, true, 0, 0, 0, 0, used, clock.GetUtcNow());

        var balance = w.TotalCredited - w.TotalSettled;
        var available = balance - w.Frozen;
        var percent = w.TotalCredited == 0 ? 0 : (int)Math.Floor(available * 100m / w.TotalCredited);
        return new BalanceView(percent, percent < 10, balance, w.Frozen, available, w.TotalCredited, used, new DateTimeOffset(w.UpdatedAt, TimeSpan.Zero));
    }

    /// <summary>对账：钱包计数器 vs 流水/预占单重算。</summary>
    public async Task<ReconcileResult> ReconcileAsync(Guid user)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var w = await db.Wallets.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == user) ?? new TgWallet();
        var credited = await db.Ledger.Where(e => e.UserId == user && e.Kind == LedgerKind.Credit).SumAsync(e => (long?)e.Amount) ?? 0;
        var settled = await db.Ledger.Where(e => e.UserId == user && e.Kind == LedgerKind.Settle).SumAsync(e => (long?)e.Amount) ?? 0;
        var open = await db.Reservations.Where(r => r.UserId == user && r.Status == ReservationStatus.Open).SumAsync(r => (long?)r.Amount) ?? 0;
        return new ReconcileResult(w.TotalCredited == credited && w.TotalSettled == settled && w.Frozen == open,
            w.TotalCredited, credited, w.TotalSettled, settled, w.Frozen, open);
    }

    private static Task EnsureWalletAsync(TokenGatewayDbContext db, Guid user, DateTime now) =>
        db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "TgWallets" ("UserId","TotalCredited","TotalSettled","Frozen","UpdatedAt")
            VALUES ({user}, 0, 0, 0, {now}) ON CONFLICT ("UserId") DO NOTHING
            """);

    /// <summary>本期 = 北京时间自然月。</summary>
    internal static DateTimeOffset PeriodStart(DateTimeOffset nowUtc)
    {
        var cn = nowUtc.ToOffset(TimeSpan.FromHours(8));
        return new DateTimeOffset(cn.Year, cn.Month, 1, 0, 0, 0, TimeSpan.FromHours(8));
    }
}
