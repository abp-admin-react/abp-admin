using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Configuration;
using AbpAdmin.Ledger.Data;
using AbpAdmin.Ledger.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.Uow;

namespace AbpAdmin.Ledger.Posting;

/// <summary>已解析到子账户归属的一条分录行（Debit/Credit 恰有一边 &gt; 0）。</summary>
public sealed record PostingLine(
    FinAccount Account,
    FinOwnerType OwnerType,
    string OwnerId,
    string Bucket,
    decimal Debit,
    decimal Credit,
    string? Dimensions);

/// <summary>
/// 过账引擎（定稿 §2/§4）：事件 → 模板 → 复式分录。必须在事务性工作单元内调用。
/// <list type="bullet">
/// <item>幂等：同键先拿 pg_advisory_xact_lock 串行化，再查 UK(Tenant,BizType,BizRefId,Phase,BatchKey)；
/// UK（NULLS NOT DISTINCT）兜底。同指纹返回原凭证，异指纹 409。</item>
/// <item>平衡：Σ借 = Σ贷 且每行单边为正，否则拒绝。</item>
/// <item>并发：子账户按 Id 排序 FOR UPDATE（lock_timeout 5s），在锁内更新余额投影与 BalanceAfter。</item>
/// <item>非负：User/Agent 归属的子账户余额不得为负（科目 AllowNegative 例外）；平台/渠道户可负。</item>
/// <item>红冲：借贷互换生成新凭证（BatchKey = "rev:" + 原凭证 Id），原凭证标 Reversed，不删分录。</item>
/// </list>
/// </summary>
public class LedgerPostingEngine : DomainService
{
    public const string ReversalBatchKeyPrefix = "rev:";
    private const string DefaultChannel = "default";

    private readonly IDbContextProvider<LedgerDbContext> _dbContextProvider;
    private readonly PostingRuleProvider _rules;
    private readonly ISettingProvider _settings;
    private readonly IOptions<LedgerOptions> _options;

    protected IUnitOfWorkManager UnitOfWorkManager => LazyServiceProvider.LazyGetRequiredService<IUnitOfWorkManager>();

    public LedgerPostingEngine(
        IDbContextProvider<LedgerDbContext> dbContextProvider,
        PostingRuleProvider rules,
        ISettingProvider settings,
        IOptions<LedgerOptions> options)
    {
        _dbContextProvider = dbContextProvider;
        _rules = rules;
        _settings = settings;
        _options = options;
    }

    public virtual async Task<Guid> PostAsync(FinanceBizEvent evt)
    {
        Validate(evt);
        await EnsureEnabledAsync(evt.BizType);

        var batchKey = evt.BatchKey ?? "";
        var db = await _dbContextProvider.GetDbContextAsync();
        await LockKeyAsync(db, evt.BizType, evt.BizRefId, evt.Phase, batchKey);

        var fingerprint = Fingerprint(evt);
        var existing = await FindVoucherAsync(db, evt.BizType, evt.BizRefId, evt.Phase, batchKey);
        if (existing != null)
        {
            if (existing.Fingerprint == fingerprint)
            {
                return existing.Id;
            }

            throw new BusinessException(FinanceErrorCodes.IdempotencyConflict)
                .WithData("BizType", evt.BizType).WithData("BizRefId", evt.BizRefId)
                .WithData("Phase", evt.Phase).WithData("BatchKey", batchKey)
                .WithData("VoucherId", existing.Id);
        }

        var rule = await _rules.FindAsync(evt.BizType, evt.Phase)
                   ?? throw new BusinessException(FinanceErrorCodes.PostingRuleNotFound)
                       .WithData("BizType", evt.BizType).WithData("Phase", evt.Phase);

        var dimensions = evt.Dimensions == null || evt.Dimensions.Count == 0
            ? null
            : JsonSerializer.Serialize(evt.Dimensions);
        var lines = new List<PostingLine>();
        foreach (var t in rule.Lines)
        {
            if (t.RequireAgentId && evt.AgentId == null)
            {
                throw new BusinessException(FinanceErrorCodes.AgentIdRequired)
                    .WithData("BizType", evt.BizType).WithData("Phase", evt.Phase);
            }

            var debit = await ResolveAsync(t.Debit, evt, evt.From);
            var credit = await ResolveAsync(t.Credit, evt, evt.To);
            lines.Add(new PostingLine(debit.Account, debit.OwnerType, debit.OwnerId, debit.Bucket, evt.Amount, 0m, dimensions));
            lines.Add(new PostingLine(credit.Account, credit.OwnerType, credit.OwnerId, credit.Bucket, 0m, evt.Amount, dimensions));
        }

        var voucher = new FinVoucher(
            GuidGenerator.Create(), evt.BizType, evt.BizRefId, evt.Phase, evt.Currency, evt.Amount,
            batchKey, rule.Version, evt.AgentId,
            isDemo: evt.BizType.StartsWith("demo.", StringComparison.Ordinal),
            summary: evt.Summary, tenantId: CurrentTenant.Id)
        {
            UserId = evt.UserId,
            Fingerprint = fingerprint,
        };

        await WriteAsync(db, voucher, lines);
        return voucher.Id;
    }

    public virtual async Task<Guid> ReverseAsync(string bizType, string bizRefId, string phase, string? batchKey)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var key = batchKey ?? "";
        await LockKeyAsync(db, bizType, bizRefId, phase, key);
        var original = await FindVoucherAsync(db, bizType, bizRefId, phase, key)
                       ?? throw new BusinessException(FinanceErrorCodes.VoucherNotFound)
                           .WithData("BizType", bizType).WithData("BizRefId", bizRefId).WithData("Phase", phase);
        return await ReverseVoucherCoreAsync(db, original);
    }

    public virtual async Task<Guid> ReverseVoucherAsync(Guid voucherId)
    {
        var db = await _dbContextProvider.GetDbContextAsync();
        var probe = await db.Vouchers.AsNoTracking().FirstOrDefaultAsync(v => v.Id == voucherId)
                    ?? throw new BusinessException(FinanceErrorCodes.VoucherNotFound).WithData("VoucherId", voucherId);
        await LockKeyAsync(db, probe.BizType, probe.BizRefId, probe.Phase, probe.BatchKey);
        var original = await db.Vouchers.FirstAsync(v => v.Id == voucherId);
        return await ReverseVoucherCoreAsync(db, original);
    }

    private async Task<Guid> ReverseVoucherCoreAsync(LedgerDbContext db, FinVoucher original)
    {
        if (original.ReversalOf != null)
        {
            throw new BusinessException(FinanceErrorCodes.VoucherNotReversible).WithData("VoucherId", original.Id);
        }

        await EnsureEnabledAsync(original.BizType);

        if (original.Status == FinVoucherStatus.Reversed)
        {
            return await db.Vouchers.Where(v => v.ReversalOf == original.Id).Select(v => v.Id).FirstAsync();
        }

        var entries = await db.Entries.AsNoTracking()
            .Where(e => e.VoucherId == original.Id).OrderBy(e => e.Seq).ToListAsync();
        var subIds = entries.Select(e => e.SubAccountId).Distinct().ToList();
        var subs = await db.SubAccounts.AsNoTracking().Where(s => subIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id);
        var accounts = await _rules.GetAccountsByIdAsync(subs.Values.Select(s => s.AccountId));

        var lines = entries.Select(e =>
        {
            var sub = subs[e.SubAccountId];
            return new PostingLine(accounts[sub.AccountId], sub.OwnerType, sub.OwnerId, sub.Bucket, e.Credit, e.Debit, e.Dimensions);
        }).ToList();

        var reversal = new FinVoucher(
            GuidGenerator.Create(), original.BizType, original.BizRefId, original.Phase, original.Currency, original.Amount,
            ReversalBatchKeyPrefix + original.Id.ToString("N"), original.RuleVersion, original.AgentId,
            original.IsDemo, "红冲: " + (original.Summary ?? original.BizRefId), CurrentTenant.Id)
        {
            UserId = original.UserId,
            ReversalOf = original.Id,
            Fingerprint = "reversal:" + original.Id.ToString("N"),
        };
        if (reversal.Summary!.Length > LedgerConsts.MaxSummaryLength)
        {
            reversal.Summary = reversal.Summary[..LedgerConsts.MaxSummaryLength];
        }

        original.Status = FinVoucherStatus.Reversed;
        await WriteAsync(db, reversal, lines);
        return reversal.Id;
    }

    public virtual async Task<decimal> GetBalanceAsync(AccountRef account, string currency)
    {
        var acc = await _rules.GetAccountAsync(account.AccountCode);
        var db = await _dbContextProvider.GetDbContextAsync();
        var ownerType = account.OwnerType ?? FinOwnerType.Platform;
        var ownerId = account.OwnerId ?? "";
        var bucket = account.Bucket ?? "";
        var balance = await (
            from s in db.SubAccounts.AsNoTracking()
            join p in db.BalanceProjections.AsNoTracking() on s.Id equals p.SubAccountId
            where s.AccountId == acc.Id && s.OwnerType == ownerType && s.OwnerId == ownerId
                  && s.Currency == currency && s.Bucket == bucket
            select (decimal?)p.Balance).FirstOrDefaultAsync();
        return balance ?? 0m;
    }

    /// <summary>平衡校验 + 子账户解析/加锁 + 投影更新 + 落凭证与分录（同一事务）。</summary>
    protected virtual async Task WriteAsync(LedgerDbContext db, FinVoucher voucher, IReadOnlyList<PostingLine> lines)
    {
        EnsureBalanced(lines);
        EnsureTransactional();

        var subIds = new List<Guid>(lines.Count);
        foreach (var line in lines)
        {
            subIds.Add(await GetOrCreateSubAccountAsync(db, line, voucher.Currency));
        }

        await db.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'");
        var lockIds = subIds.Distinct().OrderBy(id => id).ToArray();
        await db.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"FinSubAccounts\" WHERE \"Id\" = ANY({lockIds}) ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync();

        voucher.PeriodId = await GetOrCreateCurrentPeriodAsync(db);
        voucher.PostedTime = DateTime.UtcNow;

        var projections = await db.BalanceProjections.Where(p => lockIds.Contains(p.SubAccountId))
            .ToDictionaryAsync(p => p.SubAccountId);
        var entries = new List<FinEntry>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var subId = subIds[i];
            if (!projections.TryGetValue(subId, out var projection))
            {
                projection = new FinBalanceProjection(GuidGenerator.Create(), subId, CurrentTenant.Id);
                await db.BalanceProjections.AddAsync(projection);
                projections[subId] = projection;
            }

            var delta = line.Account.NormalSide == FinNormalSide.Debit ? line.Debit - line.Credit : line.Credit - line.Debit;
            var next = projection.Balance + delta;
            if (next < 0 && !line.Account.AllowNegative && line.OwnerType is FinOwnerType.User or FinOwnerType.Agent)
            {
                throw new BusinessException(FinanceErrorCodes.InsufficientBalance)
                    .WithData("AccountCode", line.Account.Code).WithData("OwnerId", line.OwnerId)
                    .WithData("Balance", projection.Balance).WithData("Delta", delta);
            }

            projection.Balance = next;
            projection.Version++;
            projection.UpdatedAt = Clock.Now;

            entries.Add(new FinEntry(GuidGenerator.Create(), voucher.Id, subId, line.Debit, line.Credit, i + 1,
                line.Dimensions, CurrentTenant.Id) { BalanceAfter = next });
        }

        await db.Vouchers.AddAsync(voucher);
        await db.Entries.AddRangeAsync(entries);
        await db.SaveChangesAsync();
    }

    public static void EnsureBalanced(IReadOnlyList<PostingLine> lines)
    {
        if (lines.Count < 2
            || lines.Any(l => l.Debit < 0 || l.Credit < 0 || (l.Debit > 0) == (l.Credit > 0))
            || lines.Sum(l => l.Debit) != lines.Sum(l => l.Credit))
        {
            throw new BusinessException(FinanceErrorCodes.UnbalancedVoucher)
                .WithData("Debit", lines.Sum(l => l.Debit)).WithData("Credit", lines.Sum(l => l.Credit));
        }
    }

    private async Task<(FinAccount Account, FinOwnerType OwnerType, string OwnerId, string Bucket)> ResolveAsync(
        string code, FinanceBizEvent evt, AccountRef? reference)
    {
        if (code == "*")
        {
            if (reference == null || string.IsNullOrWhiteSpace(reference.AccountCode))
            {
                throw new BusinessException(FinanceErrorCodes.InvalidEvent)
                    .WithData("Reason", "template requires explicit From/To account");
            }

            code = reference.AccountCode;
        }
        else if (reference != null && reference.AccountCode != code)
        {
            reference = null;
        }

        var account = await _rules.GetAccountAsync(code);
        if (reference?.OwnerType != null)
        {
            return (account, reference.OwnerType.Value, reference.OwnerId ?? "", reference.Bucket ?? "");
        }

        var bucket = reference?.Bucket ?? "";
        switch (code)
        {
            case FinanceAccountCodes.UserAvailable:
            case FinanceAccountCodes.UserFrozen:
            case FinanceAccountCodes.Unearned:
                if (evt.UserId == null)
                {
                    throw new BusinessException(FinanceErrorCodes.InvalidEvent)
                        .WithData("Reason", $"UserId required for account {code}");
                }

                return (account, FinOwnerType.User, evt.UserId.Value.ToString(), bucket);
            case FinanceAccountCodes.AgentPayable:
                if (evt.AgentId == null)
                {
                    throw new BusinessException(FinanceErrorCodes.AgentIdRequired).WithData("AccountCode", code);
                }

                return (account, FinOwnerType.Agent, evt.AgentId.Value.ToString(), bucket);
            case FinanceAccountCodes.ChannelClearing:
                var channel = evt.Dimensions != null && evt.Dimensions.TryGetValue("channel", out var c) && !string.IsNullOrWhiteSpace(c)
                    ? c
                    : DefaultChannel;
                return (account, FinOwnerType.Channel, channel, bucket);
            default:
                return (account, FinOwnerType.Platform, "", bucket);
        }
    }

    private async Task<Guid> GetOrCreateSubAccountAsync(LedgerDbContext db, PostingLine line, string currency)
    {
        var ownerType = line.OwnerType;
        var existing = await db.SubAccounts.AsNoTracking()
            .Where(s => s.AccountId == line.Account.Id && s.OwnerType == ownerType && s.OwnerId == line.OwnerId
                        && s.Currency == currency && s.Bucket == line.Bucket)
            .Select(s => (Guid?)s.Id).FirstOrDefaultAsync();
        if (existing != null)
        {
            return existing.Value;
        }

        // 并发首建：ON CONFLICT DO NOTHING 不抛错、不毁事务（UK 为 NULLS NOT DISTINCT，宿主行同样生效）
        var id = GuidGenerator.Create();
        var tenantId = CurrentTenant.Id;
        var emptyJson = "{}";
        var stamp = Guid.NewGuid().ToString("N");
        var created = Clock.Now;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FinSubAccounts" ("Id","TenantId","AccountId","OwnerType","OwnerId","Currency","Bucket","ExtraProperties","ConcurrencyStamp","CreationTime")
            VALUES ({id}, {tenantId}, {line.Account.Id}, {(int)ownerType}, {line.OwnerId}, {currency}, {line.Bucket}, {emptyJson}, {stamp}, {created})
            ON CONFLICT DO NOTHING
            """);
        return await db.SubAccounts.AsNoTracking()
            .Where(s => s.AccountId == line.Account.Id && s.OwnerType == ownerType && s.OwnerId == line.OwnerId
                        && s.Currency == currency && s.Bucket == line.Bucket)
            .Select(s => s.Id).FirstAsync();
    }

    private async Task<Guid> GetOrCreateCurrentPeriodAsync(LedgerDbContext db)
    {
        var now = Clock.Now;
        var existing = await db.Periods.AsNoTracking()
            .Where(p => p.Year == now.Year && p.Month == now.Month).Select(p => (Guid?)p.Id).FirstOrDefaultAsync();
        if (existing != null)
        {
            return existing.Value;
        }

        var id = GuidGenerator.Create();
        var tenantId = CurrentTenant.Id;
        var emptyJson = "{}";
        var stamp = Guid.NewGuid().ToString("N");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "FinPeriods" ("Id","TenantId","Year","Month","Status","ExtraProperties","ConcurrencyStamp","CreationTime")
            VALUES ({id}, {tenantId}, {now.Year}, {now.Month}, {(int)FinPeriodStatus.Open}, {emptyJson}, {stamp}, {now})
            ON CONFLICT DO NOTHING
            """);
        return await db.Periods.AsNoTracking()
            .Where(p => p.Year == now.Year && p.Month == now.Month).Select(p => p.Id).FirstAsync();
    }

    private void EnsureTransactional()
    {
        // 锁（advisory xact / FOR UPDATE）只在事务内有效；非事务 UoW 下过账等于无锁，直接拒绝
        if (UnitOfWorkManager.Current?.Options.IsTransactional != true)
        {
            throw new AbpException("Ledger posting requires a transactional unit of work.");
        }
    }

    private async Task LockKeyAsync(LedgerDbContext db, string bizType, string bizRefId, string phase, string batchKey)
    {
        EnsureTransactional();
        var key = $"fin|{CurrentTenant.Id}|{bizType}|{bizRefId}|{phase}|{batchKey}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))");
    }

    private static Task<FinVoucher?> FindVoucherAsync(LedgerDbContext db, string bizType, string bizRefId, string phase, string batchKey) =>
        db.Vouchers.FirstOrDefaultAsync(v =>
            v.BizType == bizType && v.BizRefId == bizRefId && v.Phase == phase && v.BatchKey == batchKey);

    private async Task EnsureEnabledAsync(string bizType)
    {
        if (bizType == FinanceBizTypes.ManualAdjust)
        {
            return;
        }

        var enabled = _options.Value.Enabled && await _settings.IsTrueAsync(LedgerSettings.Enabled);
        if (enabled && _options.Value.BizTypes.TryGetValue(bizType, out var typeEnabled))
        {
            enabled = typeEnabled;
        }

        if (!enabled)
        {
            throw new BusinessException(FinanceErrorCodes.LedgerDisabled).WithData("BizType", bizType);
        }
    }

    private static void Validate(FinanceBizEvent evt)
    {
        string? reason = null;
        if (string.IsNullOrWhiteSpace(evt.BizType) || evt.BizType.Length > LedgerConsts.MaxBizTypeLength)
        {
            reason = "BizType";
        }
        else if (string.IsNullOrWhiteSpace(evt.BizRefId) || evt.BizRefId.Length > LedgerConsts.MaxBizRefIdLength)
        {
            reason = "BizRefId";
        }
        else if (string.IsNullOrWhiteSpace(evt.Phase) || evt.Phase.Length > LedgerConsts.MaxPhaseLength)
        {
            reason = "Phase";
        }
        else if (string.IsNullOrWhiteSpace(evt.Currency) || evt.Currency.Length > LedgerConsts.MaxCurrencyLength
                 || evt.Currency.Any(c => !char.IsAsciiLetterOrDigit(c)))
        {
            // 币种是子账户维度：只收字母/数字形状（大小写归一由调用方负责，如调账入库前 ToUpper），任意符号串会开新维度污染科目×币余
            reason = "Currency";
        }
        else if (evt.Amount <= 0 || evt.Amount > FinanceBizEvent.MaxAmount || decimal.Round(evt.Amount, 18) != evt.Amount)
        {
            // 上界对应 FinVoucher.Amount numeric(28,18) 的整数位——不挡会拖到 SaveChanges 才炸 500
            reason = "Amount";
        }
        else if ((evt.BatchKey?.Length ?? 0) > LedgerConsts.MaxBatchKeyLength
                 || (evt.BatchKey?.StartsWith(ReversalBatchKeyPrefix, StringComparison.Ordinal) ?? false))
        {
            reason = "BatchKey";
        }
        else if ((evt.Summary?.Length ?? 0) > LedgerConsts.MaxSummaryLength)
        {
            reason = "Summary";
        }

        if (reason != null)
        {
            throw new BusinessException(FinanceErrorCodes.InvalidEvent).WithData("Field", reason);
        }
    }

    public static string Fingerprint(FinanceBizEvent evt)
    {
        static string Ref(AccountRef? r) => r == null ? "" : $"{r.AccountCode}/{r.OwnerType}/{r.OwnerId}/{r.Bucket}";
        var raw = string.Join("|",
            evt.Amount.ToString("0.##################", CultureInfo.InvariantCulture),
            evt.Currency, evt.UserId, evt.AgentId, Ref(evt.From), Ref(evt.To));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }
}
