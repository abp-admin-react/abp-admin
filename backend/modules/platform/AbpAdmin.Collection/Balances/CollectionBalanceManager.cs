using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Ledger;
using AbpAdmin.Collection.Settings;
using AbpAdmin.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.EventBus.Distributed;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Balances;

/// <summary>
/// 稳定币余额记账。扣款加入调用方事务；技术回冲自己开事务，并先锁原扣款再看有没有商业退款。
/// <c>Collection.WalletLedger.Enforce</c> 打开后，扣款、回冲和退回钱包在同一事务里写财务分录。
/// </summary>
public class CollectionBalanceManager : DomainService
{
    /// <summary>扣款命令超时缺省值（秒）。热点账户排队过长时宁可失败重试也不无限等。</summary>
    public const int DefaultDebitCommandTimeoutSeconds = 15;

    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ISettingProvider _settingProvider;
    private readonly IDistributedEventBus _distributedEventBus;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;
    private readonly IDeploymentModuleStateReader _moduleStates;
    private readonly CollectionLedgerPoster _ledgerPoster;

    public CollectionBalanceManager(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ISettingProvider settingProvider,
        IDistributedEventBus distributedEventBus,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager,
        IDeploymentModuleStateReader moduleStates,
        CollectionLedgerPoster ledgerPoster)
    {
        _dbContextProvider = dbContextProvider;
        _settingProvider = settingProvider;
        _distributedEventBus = distributedEventBus;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
        _moduleStates = moduleStates;
        _ledgerPoster = ledgerPoster;
    }

    /// <summary>
    /// 扣 USDT / USDC。余额足够才减（行锁留到外层事务提交，命令超时读配置）；
    /// 同一业务单已有未回冲扣款时：金额币种相同返回原流水，否则冲突。
    /// 先插流水行（ON CONFLICT 探部分唯一索引）再减余额——重试路径不重复扣。
    /// </summary>
    public virtual async Task<Guid> DebitAsync(
        string businessType, Guid businessId, Guid userId, CollectionCurrency currency, decimal amount)
    {
        // 余额不足时，事务必须撤销扣款流水，否则订单核对会把孤立的流水当成已付款证据。
        AssertAmbientTransactionIfAny();
        if (_unitOfWorkManager.Current != null)
        {
            return await DebitCoreAsync(businessType, businessId, userId, currency, amount);
        }

        using var uow = _unitOfWorkManager.Begin(isTransactional: true);
        var txnId = await DebitCoreAsync(businessType, businessId, userId, currency, amount);
        await uow.CompleteAsync();
        return txnId;
    }

    private async Task<Guid> DebitCoreAsync(
        string businessType, Guid businessId, Guid userId, CollectionCurrency currency, decimal amount)
    {
        AssertStableCurrency(currency);
        if (!CollectionAmounts.IsPositiveStorageAmount(amount))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Amount", amount);
        }

        if (!CollectionPaymentAdmission.TryConsume(businessType, businessId, channel: null, balanceCurrency: currency)
            && !(await _moduleStates.GetAsync()).IsEnabled(DeploymentModuleIds.Collection))
        {
            var existing = await FindUnreversedDebitAsync(businessType, businessId);
            if (existing == null)
            {
                throw new BusinessException(DeploymentModuleErrorCodes.Disabled)
                    .WithData("ModuleId", DeploymentModuleIds.Collection);
            }

            AssertDebitIdentity(existing, userId, currency, amount);
            return existing.Id;
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        if (await WalletLedgerEnforcedAsync())
        {
            await EnsureWalletMatchesLedgerAsync(userId, currency);
        }
        var now = _clock.Now;
        var txnId = GuidGenerator.Create();

        // 第一步：插扣款流水行。业务级幂等由部分唯一索引承担（谓词与 DbContext 配置一致）
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ColBalanceTxns" ("Id", "UserId", "Currency", "Action", "Amount", "BalanceAfter", "IdempotencyKey", "BusinessType", "BusinessId", "RefTxnId", "ReversedAt", "CreationTime")
            VALUES ({txnId}, {userId}, {(int)currency}, {BalanceActions.BusinessDebit}, {-amount}, {0m}, {txnId.ToString()}, {businessType}, {businessId}, NULL, NULL, {now})
            ON CONFLICT ("BusinessType", "BusinessId") WHERE "Action" = 'BusinessDebit' AND "ReversedAt" IS NULL DO NOTHING
            """);

        if (inserted == 0)
        {
            var winner = await dbContext.BalanceTxns.AsNoTracking()
                .Where(t => t.Action == BalanceActions.BusinessDebit
                            && t.ReversedAt == null
                            && t.BusinessType == businessType
                            && t.BusinessId == businessId)
                .OrderByDescending(t => t.Seq)
                .FirstAsync();

            AssertDebitIdentity(winner, userId, currency, amount);

            // 金额币种相同的重试：返回原流水，不重复扣
            return winner.Id;
        }

        // 第二步：余额足够才减（行锁留到外层提交；命令超时写进配置，避免热点账户无限排队）。
        // 超时只作用于这一条锁语句：保存/恢复，不污染共享 DbContext 上后续命令的超时
        var previousTimeout = dbContext.Database.GetCommandTimeout();
        await SetDebitCommandTimeoutAsync(dbContext);
        var balances = await dbContext.Database
            .SqlQuery<decimal>($"""
                WITH u AS (UPDATE "ColBalances" SET "Balance" = "Balance" - {amount}, "UpdatedAt" = {now}
                           WHERE "UserId" = {userId} AND "Currency" = {(int)currency} AND "Balance" >= {amount}
                           RETURNING "Balance" AS "Value")
                SELECT "Value" FROM u
                """)
            .ToListAsync();
        dbContext.Database.SetCommandTimeout(previousTimeout);

        if (balances.Count == 0)
        {
            // 余额不存在时按零记录。
            var current = await dbContext.Balances.AsNoTracking()
                .Where(b => b.UserId == userId && b.Currency == currency)
                .FirstOrDefaultAsync();
            Logger.LogInformation(
                "扣款被拒：用户 {UserId} {Currency} 余额不足，当前 {Balance}，本次需要 {Amount}",
                userId, currency, current?.Balance ?? 0m, amount);

            throw new BusinessException(CollectionErrorCodes.InsufficientBalance)
                .WithData("Currency", currency)
                .WithData("Balance", CollectionAmounts.Format(current?.Balance ?? 0m))
                .WithData("Amount", CollectionAmounts.Format(amount));
        }

        await SetBalanceAfterAsync(dbContext, txnId, balances[0]);
        if (await WalletLedgerEnforcedAsync())
        {
            await _ledgerPoster.PostOrderDebitAsync(txnId, userId, currency, amount);
        }

        return txnId;
    }

    private static void AssertDebitIdentity(ColBalanceTxn existing, Guid userId, CollectionCurrency currency, decimal amount)
    {
        if (existing.UserId != userId || existing.Currency != currency || existing.Amount != -amount)
        {
            throw new BusinessException(CollectionErrorCodes.DebitConflict)
                .WithData("BusinessType", existing.BusinessType!)
                .WithData("BusinessId", existing.BusinessId!.Value);
        }
    }

    /// <summary>
    /// 回冲未撤销的消费扣款。先锁扣款行：已有商业退款则拒绝，避免和退回钱包各入账一次。
    /// 双写打开时没有原凭证就拒绝，不发明反向分录。订单是否已支付由调用方约束。
    /// </summary>
    public virtual async Task ReverseAsync(string businessType, Guid businessId)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var dbContext = await _dbContextProvider.GetDbContextAsync();

        var debit = await dbContext.BalanceTxns.AsNoTracking()
            .Where(t => t.Action == BalanceActions.BusinessDebit
                        && t.ReversedAt == null
                        && t.BusinessType == businessType
                        && t.BusinessId == businessId)
            .OrderByDescending(t => t.Seq)
            .FirstOrDefaultAsync();
        if (debit == null)
        {
            await uow.CompleteAsync();
            return;
        }

        // 与商业退款共用这把行锁。先看到退款行就不再加回余额，避免两边都入账。
        await LockDebitAsync(dbContext, debit.Id);
        var locked = await dbContext.BalanceTxns.AsNoTracking().FirstAsync(txn => txn.Id == debit.Id);
        if (locked.ReversedAt != null)
        {
            await uow.CompleteAsync();
            return;
        }

        if (await dbContext.WalletRefunds.AnyAsync(refund => refund.OriginalDebitTxnId == locked.Id))
        {
            throw new BusinessException(CollectionErrorCodes.WalletRefundNotAllowed)
                .WithData("BalanceTxnId", locked.Id);
        }

        if (await WalletLedgerEnforcedAsync() && !await _ledgerPoster.HasOrderDebitVoucherAsync(locked.Id))
        {
            throw new BusinessException(CollectionErrorCodes.WalletLedgerPending)
                .WithData("BalanceTxnId", locked.Id);
        }

        var now = _clock.Now;
        var reversed = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ColBalanceTxns\" SET \"ReversedAt\" = {now} WHERE \"Id\" = {locked.Id} AND \"ReversedAt\" IS NULL");
        if (reversed == 0)
        {
            // 另一个并发回冲已经赢：不加回余额，直接成功
            await uow.CompleteAsync();
            return;
        }

        var amount = -debit.Amount;
        var reverseId = GuidGenerator.Create();
        var reverseInserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ColBalanceTxns" ("Id", "UserId", "Currency", "Action", "Amount", "BalanceAfter", "IdempotencyKey", "BusinessType", "BusinessId", "RefTxnId", "ReversedAt", "CreationTime")
            VALUES ({reverseId}, {debit.UserId}, {(int)debit.Currency}, {BalanceActions.DebitReverse}, {amount}, {0m}, {"reverse:" + debit.Id}, {businessType}, {businessId}, {debit.Id}, NULL, {now})
            ON CONFLICT ("IdempotencyKey") DO NOTHING
            """);
        if (reverseInserted == 0)
        {
            // 同一扣款的回冲流水已存在：不再加余额
            await uow.CompleteAsync();
            return;
        }

        var reverseBalanceAfter = await AddToBalanceAsync(dbContext, debit.UserId, debit.Currency, amount, now);
        await SetBalanceAfterAsync(dbContext, reverseId, reverseBalanceAfter);
        if (await WalletLedgerEnforcedAsync())
        {
            await _ledgerPoster.PostOrderDebitReverseAsync(debit.Id);
        }

        await uow.CompleteAsync();
    }


    public virtual Task<ColBalanceTxn?> FindUnreversedDebitAsync(string businessType, Guid businessId)
    {
        return QueryAsync(async () =>
        {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        return await dbContext.BalanceTxns.AsNoTracking()
            .Where(t => t.Action == BalanceActions.BusinessDebit
                        && t.ReversedAt == null
                        && t.BusinessType == businessType
                        && t.BusinessId == businessId)
            .OrderByDescending(t => t.Seq)
            .FirstOrDefaultAsync();
        });
    }

    /// <summary>
    /// 正向读取新增充值，按独立的倒序游标重扫固定七日窗口，各取至多 maxCount 条后按序号合并。
    /// </summary>
    public virtual Task<ListCreditedTxnsResult> ListCreditedAsync(long afterSeq, int maxCount, long rescanBeforeSeq = 0, DateTime? rescanStartedAt = null)
    {
        return QueryAsync(async () =>
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            var through = rescanStartedAt ?? _clock.Now;
            var cutoff = through.AddDays(-7);

            var forward = await dbContext.BalanceTxns.AsNoTracking()
                .Where(t => t.Action == BalanceActions.DepositCredit && t.Seq > afterSeq)
                .OrderBy(t => t.Seq)
                .Take(maxCount)
                .ToListAsync();

            var rescan = await dbContext.BalanceTxns.AsNoTracking()
                .Where(t => t.Action == BalanceActions.DepositCredit
                            && t.Seq <= afterSeq
                            && (rescanBeforeSeq == 0 || t.Seq < rescanBeforeSeq)
                            && t.CreationTime >= cutoff && t.CreationTime <= through)
                .OrderByDescending(t => t.Seq)
                .Take(maxCount)
                .ToListAsync();

            var merged = forward.Concat(rescan)
                .DistinctBy(t => t.Seq)
                .OrderBy(t => t.Seq)
                .ToList();

            return new ListCreditedTxnsResult
            {
                Items = merged.Select(t => new CreditedTxnDto
                {
                    Seq = t.Seq,
                    BalanceTxnId = t.Id,
                    UserId = t.UserId,
                    Currency = t.Currency,
                    Amount = CollectionAmounts.Format(t.Amount),
                    CreditedAt = t.CreationTime,
                }).ToList(),
                RescanBeforeSeq = rescan.Count > 0 && rescan.Count == maxCount ? rescan[^1].Seq : 0,
                Cursor = merged.Count > 0 ? Math.Max(afterSeq, merged.Max(t => t.Seq)) : afterSeq,
            };
        });
    }

    /// <summary>
    /// 记一笔充值入账：IdempotencyKey 是进款单 id（重复提交只入账一次），
    /// 同事务写流水并加余额，BalanceCreditedEto 挂在环境工作单元完成之后发布。
    /// </summary>
    public virtual async Task<Guid> CreditAsync(
        Guid userId, CollectionCurrency currency, decimal amount, string idempotencyKey)
    {
        // 流水和余额必须一起提交，否则重试会把只写入流水的充值误判为已入账。
        AssertAmbientTransactionIfAny();
        if (_unitOfWorkManager.Current != null)
        {
            return await CreditCoreAsync(userId, currency, amount, idempotencyKey);
        }

        using var uow = _unitOfWorkManager.Begin(isTransactional: true);
        var txnId = await CreditCoreAsync(userId, currency, amount, idempotencyKey);
        await uow.CompleteAsync();
        return txnId;
    }

    private async Task<Guid> CreditCoreAsync(
        Guid userId, CollectionCurrency currency, decimal amount, string idempotencyKey)
    {
        AssertStableCurrency(currency);
        if (!CollectionAmounts.IsPositiveStorageAmount(amount))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Amount", amount);
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;
        var txnId = GuidGenerator.Create();

        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ColBalanceTxns" ("Id", "UserId", "Currency", "Action", "Amount", "BalanceAfter", "IdempotencyKey", "BusinessType", "BusinessId", "RefTxnId", "ReversedAt", "CreationTime")
            VALUES ({txnId}, {userId}, {(int)currency}, {BalanceActions.DepositCredit}, {amount}, {0m}, {idempotencyKey}, {CollectionBusinessTypes.TopUp}, {txnId}, NULL, NULL, {now})
            ON CONFLICT ("IdempotencyKey") DO NOTHING
            """);
        if (inserted == 0)
        {
            var existing = await dbContext.BalanceTxns.AsNoTracking()
                .FirstAsync(t => t.IdempotencyKey == idempotencyKey);
            if (existing.Action != BalanceActions.DepositCredit || existing.UserId != userId
                || existing.Currency != currency || existing.Amount != amount)
            {
                throw new BusinessException(CollectionErrorCodes.BalanceStateConflict);
            }
            return existing.Id;
        }

        var creditBalanceAfter = await AddToBalanceAsync(dbContext, userId, currency, amount, now);
        await SetBalanceAfterAsync(dbContext, txnId, creditBalanceAfter);

        await PublishAfterCommitAsync(new BalanceCreditedEto
        {
            BusinessId = txnId,
            BalanceTxnId = txnId,
            UserId = userId,
            Currency = currency,
            Amount = CollectionAmounts.Format(amount),
            CompletedAt = now,
        });

        return txnId;
    }

    // ── 提现冻结原语（提现 M1：可用/冻结两桶搬运 + 出账 + 退款）────────────────────────
    //
    // 纪律与扣款/充值一致：先插流水行（ON CONFLICT 探 IdempotencyKey 唯一索引）再动余额，
    // 两条语句必须同事务（加入环境事务或自开）；幂等键 = W:{提现单 Id:N}:{动作}
    //（BalanceIdempotencyKeys 唯一定义处）。重放同键同额返回原流水，同键异额抛冲突。

    /// <summary>
    /// 提现提交冻结：可用 −amount / 冻结 +amount（一条条件 UPDATE 原子完成）。
    /// 可用不足抛 InsufficientBalance，失败路径不留孤儿流水行。
    /// </summary>
    public virtual async Task<Guid> FreezeAsync(
        Guid userId, CollectionCurrency currency, decimal amount, Guid withdrawOrderId)
    {
        AssertAmbientTransactionIfAny();
        if (_unitOfWorkManager.Current != null)
        {
            return await FreezeCoreAsync(userId, currency, amount, withdrawOrderId);
        }

        using var uow = _unitOfWorkManager.Begin(isTransactional: true);
        var txnId = await FreezeCoreAsync(userId, currency, amount, withdrawOrderId);
        await uow.CompleteAsync();
        return txnId;
    }

    private async Task<Guid> FreezeCoreAsync(
        Guid userId, CollectionCurrency currency, decimal amount, Guid withdrawOrderId)
    {
        AssertStableCurrency(currency);
        if (amount <= 0)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Amount", amount);
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var (txnId, inserted) = await InsertWithdrawTxnRowAsync(
            dbContext, userId, currency, BalanceActions.WithdrawFreeze, -amount, withdrawOrderId);
        if (!inserted)
        {
            return txnId; // 幂等重放：不动两桶
        }

        // 可用足够才冻结（行锁留到外层提交；与扣款同一超时口径——热点账户排队宁可失败重试）
        var previousTimeout = dbContext.Database.GetCommandTimeout();
        await SetDebitCommandTimeoutAsync(dbContext);
        var balances = await dbContext.Database
            .SqlQuery<decimal>($"""
                WITH u AS (UPDATE "ColBalances" SET "Balance" = "Balance" - {amount}, "Frozen" = "Frozen" + {amount}, "UpdatedAt" = {_clock.Now}
                           WHERE "UserId" = {userId} AND "Currency" = {(int)currency} AND "Balance" >= {amount}
                           RETURNING "Balance" AS "Value")
                SELECT "Value" FROM u
                """)
            .ToListAsync();
        dbContext.Database.SetCommandTimeout(previousTimeout);

        if (balances.Count == 0)
        {
            var current = await dbContext.Balances.AsNoTracking()
                .Where(b => b.UserId == userId && b.Currency == currency)
                .FirstOrDefaultAsync();
            Logger.LogInformation(
                "提现冻结被拒：用户 {UserId} {Currency} 可用不足，当前可用 {Balance} 冻结 {Frozen}，本次需要 {Amount}",
                userId, currency, current?.Balance ?? 0m, current?.Frozen ?? 0m, amount);

            throw new BusinessException(CollectionErrorCodes.InsufficientBalance)
                .WithData("Currency", currency)
                .WithData("Balance", CollectionAmounts.Format(current?.Balance ?? 0m))
                .WithData("Amount", CollectionAmounts.Format(amount));
        }

        await SetBalanceAfterAsync(dbContext, txnId, balances[0]);
        return txnId;
    }

    /// <summary>
    /// 提现撤单/拒绝解冻：冻结 −amount / 可用 +amount。重放同键幂等。
    /// 冻结桶不足说明两桶状态与流水不符（完整性异常），抛 BalanceStateConflict。
    /// </summary>
    public virtual async Task<Guid> UnfreezeAsync(
        Guid userId, CollectionCurrency currency, decimal amount, Guid withdrawOrderId)
    {
        AssertAmbientTransactionIfAny();
        if (_unitOfWorkManager.Current != null)
        {
            return await UnfreezeCoreAsync(userId, currency, amount, withdrawOrderId);
        }

        using var uow = _unitOfWorkManager.Begin(isTransactional: true);
        var txnId = await UnfreezeCoreAsync(userId, currency, amount, withdrawOrderId);
        await uow.CompleteAsync();
        return txnId;
    }

    private async Task<Guid> UnfreezeCoreAsync(
        Guid userId, CollectionCurrency currency, decimal amount, Guid withdrawOrderId)
    {
        AssertStableCurrency(currency);
        if (amount <= 0)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Amount", amount);
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var (txnId, inserted) = await InsertWithdrawTxnRowAsync(
            dbContext, userId, currency, BalanceActions.WithdrawUnfreeze, amount, withdrawOrderId);
        if (!inserted)
        {
            return txnId; // 幂等重放：不动两桶
        }

        var balances = await dbContext.Database
            .SqlQuery<decimal>($"""
                WITH u AS (UPDATE "ColBalances" SET "Balance" = "Balance" + {amount}, "Frozen" = "Frozen" - {amount}, "UpdatedAt" = {_clock.Now}
                           WHERE "UserId" = {userId} AND "Currency" = {(int)currency} AND "Frozen" >= {amount}
                           RETURNING "Balance" AS "Value")
                SELECT "Value" FROM u
                """)
            .ToListAsync();

        if (balances.Count == 0)
        {
            throw await BalanceStateConflictAsync(dbContext, userId, currency, "解冻", amount, withdrawOrderId);
        }

        await SetBalanceAfterAsync(dbContext, txnId, balances[0]);
        return txnId;
    }

    /// <summary>
    /// 提现审核通过出账：冻结 −amount（可用不动——冻结时就已排除在可用外）。
    /// 钱在此步真正离开用户侧；打款完成不再动余额。
    /// </summary>
    public virtual async Task<Guid> DeductFrozenAsync(
        Guid userId, CollectionCurrency currency, decimal amount, Guid withdrawOrderId)
    {
        AssertAmbientTransactionIfAny();
        if (_unitOfWorkManager.Current != null)
        {
            return await DeductFrozenCoreAsync(userId, currency, amount, withdrawOrderId);
        }

        using var uow = _unitOfWorkManager.Begin(isTransactional: true);
        var txnId = await DeductFrozenCoreAsync(userId, currency, amount, withdrawOrderId);
        await uow.CompleteAsync();
        return txnId;
    }

    private async Task<Guid> DeductFrozenCoreAsync(
        Guid userId, CollectionCurrency currency, decimal amount, Guid withdrawOrderId)
    {
        AssertStableCurrency(currency);
        if (amount <= 0)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Amount", amount);
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var (txnId, inserted) = await InsertWithdrawTxnRowAsync(
            dbContext, userId, currency, BalanceActions.WithdrawSettle, -amount, withdrawOrderId);
        if (!inserted)
        {
            return txnId; // 幂等重放：不动两桶
        }

        var balances = await dbContext.Database
            .SqlQuery<decimal>($"""
                WITH u AS (UPDATE "ColBalances" SET "Frozen" = "Frozen" - {amount}, "UpdatedAt" = {_clock.Now}
                           WHERE "UserId" = {userId} AND "Currency" = {(int)currency} AND "Frozen" >= {amount}
                           RETURNING "Balance" AS "Value")
                SELECT "Value" FROM u
                """)
            .ToListAsync();

        if (balances.Count == 0)
        {
            throw await BalanceStateConflictAsync(dbContext, userId, currency, "扣冻结出账", amount, withdrawOrderId);
        }

        await SetBalanceAfterAsync(dbContext, txnId, balances[0]);
        return txnId;
    }

    /// <summary>
    /// 提现广播失败退款：加回可用余额（走充值同一 upsert，无余额行也能建行）。
    /// 只用于审核后失败终态；撤单/拒绝走 <see cref="UnfreezeAsync"/>。
    /// </summary>
    public virtual async Task<Guid> RefundAsync(
        Guid userId, CollectionCurrency currency, decimal amount, Guid withdrawOrderId)
    {
        AssertAmbientTransactionIfAny();
        if (_unitOfWorkManager.Current != null)
        {
            return await RefundCoreAsync(userId, currency, amount, withdrawOrderId);
        }

        using var uow = _unitOfWorkManager.Begin(isTransactional: true);
        var txnId = await RefundCoreAsync(userId, currency, amount, withdrawOrderId);
        await uow.CompleteAsync();
        return txnId;
    }

    private async Task<Guid> RefundCoreAsync(
        Guid userId, CollectionCurrency currency, decimal amount, Guid withdrawOrderId)
    {
        AssertStableCurrency(currency);
        if (amount <= 0)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Amount", amount);
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var (txnId, inserted) = await InsertWithdrawTxnRowAsync(
            dbContext, userId, currency, BalanceActions.WithdrawRefund, amount, withdrawOrderId);
        if (!inserted)
        {
            return txnId; // 幂等重放：不加余额
        }

        var balanceAfter = await AddToBalanceAsync(dbContext, userId, currency, amount, _clock.Now);
        await SetBalanceAfterAsync(dbContext, txnId, balanceAfter);
        return txnId;
    }

    /// <summary>
    /// 提现动作的流水行插入（幂等探针）：IdempotencyKey = W:{提现单}:{动作}，冲突目标全列唯一索引。
    /// 返回 (流水 Id, 是否新插入)；重放同键：金额/币种/用户一致返回 (原流水, false)，
    /// 不一致抛 WithdrawActionConflict（调用方状态机错乱）。
    /// </summary>
    private async Task<(Guid TxnId, bool Inserted)> InsertWithdrawTxnRowAsync(
        CollectionDbContext dbContext, Guid userId, CollectionCurrency currency,
        string action, decimal signedAmount, Guid withdrawOrderId)
    {
        var now = _clock.Now;
        var idempotencyKey = BalanceIdempotencyKeys.ForWithdrawOrder(withdrawOrderId, action);
        var txnId = GuidGenerator.Create();

        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ColBalanceTxns" ("Id", "UserId", "Currency", "Action", "Amount", "BalanceAfter", "IdempotencyKey", "BusinessType", "BusinessId", "RefTxnId", "ReversedAt", "CreationTime")
            VALUES ({txnId}, {userId}, {(int)currency}, {action}, {signedAmount}, {0m}, {idempotencyKey}, {CollectionBusinessTypes.Withdraw}, {withdrawOrderId}, NULL, NULL, {now})
            ON CONFLICT ("IdempotencyKey") DO NOTHING
            """);

        if (inserted == 0)
        {
            var existing = await dbContext.BalanceTxns.AsNoTracking()
                .FirstAsync(t => t.IdempotencyKey == idempotencyKey);

            if (existing.UserId != userId || existing.Currency != currency
                || existing.Amount != signedAmount || existing.Action != action)
            {
                throw new BusinessException(CollectionErrorCodes.WithdrawActionConflict)
                    .WithData("WithdrawOrderId", withdrawOrderId)
                    .WithData("Action", action);
            }

            return (existing.Id, false);
        }

        return (txnId, true);
    }

    /// <summary>冻结桶不足的完整性异常：带当前两桶现场便于人工核查。</summary>
    private async Task<BusinessException> BalanceStateConflictAsync(
        CollectionDbContext dbContext, Guid userId, CollectionCurrency currency,
        string operation, decimal amount, Guid withdrawOrderId)
    {
        var current = await dbContext.Balances.AsNoTracking()
            .Where(b => b.UserId == userId && b.Currency == currency)
            .FirstOrDefaultAsync();
        Logger.LogError(
            "余额两桶状态冲突：提现单 {WithdrawOrderId} {Operation} {Amount}，用户 {UserId} {Currency} 当前可用 {Balance} 冻结 {Frozen}",
            withdrawOrderId, operation, amount, userId, currency, current?.Balance ?? 0m, current?.Frozen ?? 0m);

        return new BusinessException(CollectionErrorCodes.BalanceStateConflict)
            .WithData("WithdrawOrderId", withdrawOrderId)
            .WithData("Operation", operation)
            .WithData("Frozen", CollectionAmounts.Format(current?.Frozen ?? 0m))
            .WithData("Amount", CollectionAmounts.Format(amount));
    }

    /// <summary>人工调账原语。调用方在同一事务中完成审批状态与总账过账。</summary>
    public virtual async Task<Guid> ApplyAdjustmentAsync(
        Guid adjustmentId, Guid userId, CollectionCurrency currency, decimal amount)
    {
        if (_unitOfWorkManager.Current?.Options.IsTransactional != true)
        {
            throw new InvalidOperationException("Balance adjustment requires a transactional unit of work.");
        }

        AssertStableCurrency(currency);
        if (amount == 0m || decimal.Round(amount, CollectionAmounts.Scale) != amount
            || amount > AbpAdmin.Ledger.Integration.Contracts.FinanceBizEvent.MaxAmount
            || amount < -AbpAdmin.Ledger.Integration.Contracts.FinanceBizEvent.MaxAmount)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount);
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var key = "adjustment:" + adjustmentId.ToString("D");
        var txnId = GuidGenerator.Create();
        var now = _clock.Now;
        var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "ColBalanceTxns" ("Id", "UserId", "Currency", "Action", "Amount", "BalanceAfter", "IdempotencyKey", "BusinessType", "BusinessId", "RefTxnId", "ReversedAt", "CreationTime")
            VALUES ({txnId}, {userId}, {(int)currency}, {BalanceActions.ManualAdjust}, {amount}, {0m}, {key}, {AbpAdmin.Ledger.Integration.Contracts.FinanceBizTypes.WalletAdjustment}, {adjustmentId}, NULL, NULL, {now})
            ON CONFLICT ("IdempotencyKey") DO NOTHING
            """);
        if (inserted == 0)
        {
            var existing = await dbContext.BalanceTxns.AsNoTracking()
                .SingleAsync(row => row.IdempotencyKey == key);
            if (existing.UserId != userId || existing.Currency != currency || existing.Amount != amount
                || existing.Action != BalanceActions.ManualAdjust)
            {
                throw new BusinessException(CollectionBalanceAdjustmentErrorCodes.RequestConflict);
            }

            return existing.Id;
        }

        decimal balanceAfter;
        if (amount > 0m)
        {
            balanceAfter = await AddToBalanceAsync(dbContext, userId, currency, amount, now);
        }
        else
        {
            var previousTimeout = dbContext.Database.GetCommandTimeout();
            await SetDebitCommandTimeoutAsync(dbContext);
            try
            {
                var balances = await dbContext.Database.SqlQuery<decimal>($"""
                    WITH updated AS (
                        UPDATE "ColBalances" SET "Balance" = "Balance" + {amount}, "UpdatedAt" = {now}
                        WHERE "UserId" = {userId} AND "Currency" = {(int)currency} AND "Balance" >= {-amount}
                        RETURNING "Balance" AS "Value")
                    SELECT "Value" FROM updated
                    """).ToListAsync();
                if (balances.Count == 0)
                {
                    throw new BusinessException(CollectionErrorCodes.InsufficientBalance);
                }

                balanceAfter = balances[0];
            }
            finally
            {
                dbContext.Database.SetCommandTimeout(previousTimeout);
            }
        }

        await SetBalanceAfterAsync(dbContext, txnId, balanceAfter);
        return txnId;
    }

    // ── 内部 ─────────────────────────────────────────────────────────────────────────────


    /// <summary>余额行加钱（回冲/充值/提现退款共用同一 upsert；带符号语义由调用方保证为正数）。</summary>
    private async Task<decimal> AddToBalanceAsync(
        Microsoft.EntityFrameworkCore.DbContext dbContext, Guid userId, CollectionCurrency currency, decimal amount, DateTime now)
    {
        var balances = await dbContext.Database
            .SqlQuery<decimal>($"""
                WITH u AS (INSERT INTO "ColBalances" ("Id", "UserId", "Currency", "Balance", "Version", "UpdatedAt")
                           VALUES ({GuidGenerator.Create()}, {userId}, {(int)currency}, {amount}, 0, {now})
                           ON CONFLICT ("UserId", "Currency") DO UPDATE SET "Balance" = "ColBalances"."Balance" + {amount}, "UpdatedAt" = {now}
                           RETURNING "Balance" AS "Value")
                SELECT "Value" FROM u
                """)
            .ToListAsync();
        return balances.Count > 0 ? balances[0] : amount;
    }

    /// <summary>把本次记账后的可用余额写回流水。</summary>
    private static Task<int> SetBalanceAfterAsync(
        Microsoft.EntityFrameworkCore.DbContext dbContext, Guid txnId, decimal balanceAfter)
    {
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ColBalanceTxns\" SET \"BalanceAfter\" = {balanceAfter} WHERE \"Id\" = {txnId}");
    }

    /// <summary>拒绝加入非事务工作单元，防止流水与余额各自提交。</summary>
    private void AssertAmbientTransactionIfAny()
    {
        var current = _unitOfWorkManager.Current;
        if (current != null && !current.Options.IsTransactional)
        {
            throw new InvalidOperationException(
                "DebitAsync/CreditAsync must run inside a transactional unit of work (or none, so one can be self-opened). " +
                "A non-transactional ambient unit of work would autocommit each statement separately and orphan the ledger row on failure.");
        }
    }

    /// <summary>查询助手：没有环境工作单元时（直连场景/测试）自开一个非事务 UoW 再查。</summary>
    private async Task<T> QueryAsync<T>(Func<Task<T>> read)
    {
        if (_unitOfWorkManager.Current != null)
        {
            return await read();
        }

        using var uow = _unitOfWorkManager.Begin();
        var result = await read();
        await uow.CompleteAsync();
        return result;
    }

    private static void AssertStableCurrency(CollectionCurrency currency)
    {
        if (currency != CollectionCurrency.Usdt && currency != CollectionCurrency.Usdc)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount)
                .WithData("Currency", currency)
                .WithData("Allowed", "USDT, USDC");
        }
    }

    private async Task SetDebitCommandTimeoutAsync(CollectionDbContext dbContext)
    {
        var raw = await _settingProvider.GetOrNullAsync(CollectionSettings.Balance.DebitCommandTimeoutSeconds);
        var seconds = int.TryParse(raw, out var parsed) && parsed > 0
            ? parsed
            : DefaultDebitCommandTimeoutSeconds;
        dbContext.Database.SetCommandTimeout(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>事件挂在环境工作单元完成之后派发（计划第 3.2 节）；没有环境事务就立即发。</summary>
    private async Task PublishAfterCommitAsync(BalanceCreditedEto eto)
    {
        var current = _unitOfWorkManager.Current;
        if (current == null)
        {
            await _distributedEventBus.PublishAsync(eto);
            return;
        }

        current.OnCompleted(async () =>
        {
            try
            {
                await _distributedEventBus.PublishAsync(eto);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "BalanceCreditedEto 发布失败，充值补插作业会兜底（BalanceTxnId={TxnId})", eto.BalanceTxnId);
            }
        });
    }

    public virtual Task<WalletRefundHoldResult> HoldWalletRefundAsync(Guid originalDebitTxnId, string requestId, decimal amount)
    {
        return ChangeWalletRefundAsync(originalDebitTxnId, requestId, amount, creditWallet: false, confirmOnly: false);
    }

    public virtual Task ConfirmWalletRefundHoldAsync(Guid originalDebitTxnId, string requestId)
    {
        return ChangeWalletRefundAsync(originalDebitTxnId, requestId, 0m, creditWallet: false, confirmOnly: true);
    }

    public virtual Task<WalletRefundHoldResult> RefundWalletAsync(Guid originalDebitTxnId, string requestId, decimal amount)
    {
        return ChangeWalletRefundAsync(originalDebitTxnId, requestId, amount, creditWallet: true, confirmOnly: false);
    }

    /// <summary>
    /// 把说明系到已成功或已核实的退款上。不新增退款行，也不入钱包。
    /// 双写未开启、待核实、金额不一致或原扣款已撤销时拒绝。
    /// </summary>
    public virtual async Task LinkWalletRefundNoteAsync(Guid originalDebitTxnId, string requestId, decimal? amount = null)
    {
        AssertRequestId(requestId);
        if (!await WalletLedgerEnforcedAsync())
        {
            throw new BusinessException(CollectionErrorCodes.WalletLedgerNotEnforced)
                .WithData("BalanceTxnId", originalDebitTxnId);
        }

        using var uow = BeginWalletRefundUnitOfWork();
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await LockDebitAsync(dbContext, originalDebitTxnId);
        var debit = await dbContext.BalanceTxns.AsNoTracking().FirstOrDefaultAsync(txn => txn.Id == originalDebitTxnId);
        var row = await dbContext.WalletRefunds.FirstOrDefaultAsync(refund =>
            refund.OriginalDebitTxnId == originalDebitTxnId && refund.RequestId == requestId);
        if (debit == null
            || debit.Action != BalanceActions.BusinessDebit
            || debit.ReversedAt != null
            || row == null
            || row.Status == WalletRefundStatus.PendingVerification)
        {
            throw new BusinessException(CollectionErrorCodes.WalletRefundNotAllowed)
                .WithData("BalanceTxnId", originalDebitTxnId)
                .WithData("RequestId", requestId);
        }

        if (amount.HasValue && amount.Value != row.Amount)
        {
            throw new BusinessException(CollectionErrorCodes.WalletRefundConflict)
                .WithData("BalanceTxnId", originalDebitTxnId)
                .WithData("RequestId", requestId)
                .WithData("Amount", amount.Value);
        }

        if (uow != null)
        {
            await uow.CompleteAsync();
        }
    }

    private async Task<WalletRefundHoldResult> ChangeWalletRefundAsync(
        Guid originalDebitTxnId, string requestId, decimal amount, bool creditWallet, bool confirmOnly)
    {
        AssertRequestId(requestId);
        if (!await WalletLedgerEnforcedAsync())
        {
            if (!creditWallet && !confirmOnly)
            {
                return new WalletRefundHoldResult { Applied = false };
            }

            throw new BusinessException(CollectionErrorCodes.WalletLedgerNotEnforced);
        }

        if (!confirmOnly && amount <= 0)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Amount", amount);
        }

        using var uow = BeginWalletRefundUnitOfWork();
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        await LockDebitAsync(dbContext, originalDebitTxnId);
        var debit = await dbContext.BalanceTxns.FirstOrDefaultAsync(txn => txn.Id == originalDebitTxnId);
        if (debit == null
            || debit.Action != BalanceActions.BusinessDebit
            || debit.ReversedAt != null
            || debit.BusinessId == null)
        {
            throw new BusinessException(CollectionErrorCodes.WalletRefundNotAllowed)
                .WithData("BalanceTxnId", originalDebitTxnId);
        }

        var rows = await dbContext.WalletRefunds
            .Where(refund => refund.OriginalDebitTxnId == debit.Id)
            .ToListAsync();
        var existing = rows.FirstOrDefault(refund => refund.RequestId == requestId);
        if (confirmOnly)
        {
            if (existing == null || existing.Status != WalletRefundStatus.PendingVerification)
            {
                throw new BusinessException(CollectionErrorCodes.WalletRefundNotAllowed)
                    .WithData("RequestId", requestId);
            }

            existing.Status = WalletRefundStatus.VerifiedExternal;
            await dbContext.SaveChangesAsync();
            if (uow != null)
            {
                await uow.CompleteAsync();
            }

            return new WalletRefundHoldResult { Applied = true, RefundId = existing.Id, RequestId = requestId };
        }

        if (existing != null)
        {
            var sameCredit = creditWallet
                ? existing.Status == WalletRefundStatus.Succeeded
                : existing.Status == WalletRefundStatus.PendingVerification;
            if (existing.Amount != amount || !sameCredit)
            {
                throw new BusinessException(CollectionErrorCodes.WalletRefundConflict)
                    .WithData("RequestId", requestId)
                    .WithData("BalanceTxnId", debit.Id);
            }

            if (uow != null)
            {
                await uow.CompleteAsync();
            }

            return new WalletRefundHoldResult { Applied = true, RefundId = existing.Id, RequestId = requestId };
        }

        if (rows.Any(refund => refund.Status == WalletRefundStatus.PendingVerification))
        {
            throw new BusinessException(CollectionErrorCodes.WalletRefundNotAllowed)
                .WithData("BalanceTxnId", debit.Id)
                .WithData("Reason", "pending verification");
        }

        var occupied = rows.Sum(refund => refund.Amount);
        if (occupied + amount > -debit.Amount)
        {
            throw new BusinessException(CollectionErrorCodes.WalletRefundExceeds)
                .WithData("BalanceTxnId", debit.Id)
                .WithData("Amount", amount);
        }

        if (!await _ledgerPoster.HasOrderDebitVoucherAsync(debit.Id))
        {
            throw new BusinessException(CollectionErrorCodes.WalletLedgerPending)
                .WithData("BalanceTxnId", debit.Id);
        }

        await EnsureWalletMatchesLedgerAsync(debit.UserId, debit.Currency);
        var refundId = GuidGenerator.Create();
        Guid? balanceTxnId = null;
        if (creditWallet)
        {
            var now = _clock.Now;
            balanceTxnId = GuidGenerator.Create();
            var inserted = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "ColBalanceTxns" ("Id", "UserId", "Currency", "Action", "Amount", "BalanceAfter", "IdempotencyKey", "BusinessType", "BusinessId", "RefTxnId", "ReversedAt", "CreationTime")
                VALUES ({balanceTxnId}, {debit.UserId}, {(int)debit.Currency}, {BalanceActions.WalletRefund}, {amount}, {0m}, {"wallet-refund:" + debit.Id.ToString("N") + ":" + requestId}, {debit.BusinessType}, {debit.BusinessId}, {debit.Id}, NULL, {now})
                ON CONFLICT ("IdempotencyKey") DO NOTHING
                """);
            if (inserted == 0)
            {
                throw new BusinessException(CollectionErrorCodes.WalletRefundConflict)
                    .WithData("RequestId", requestId);
            }

            var balanceAfter = await AddToBalanceAsync(dbContext, debit.UserId, debit.Currency, amount, now);
            await SetBalanceAfterAsync(dbContext, balanceTxnId.Value, balanceAfter);
        }

        dbContext.WalletRefunds.Add(new ColWalletRefund(refundId)
        {
            OriginalDebitTxnId = debit.Id,
            RequestId = requestId,
            UserId = debit.UserId,
            Currency = debit.Currency,
            Amount = amount,
            Status = creditWallet ? WalletRefundStatus.Succeeded : WalletRefundStatus.PendingVerification,
            BalanceTxnId = balanceTxnId,
            CreationTime = _clock.Now,
        });
        await dbContext.SaveChangesAsync();
        if (creditWallet)
        {
            await _ledgerPoster.PostOrderRefundAsync(refundId, debit.UserId, debit.Currency, amount);
        }

        if (uow != null)
        {
            await uow.CompleteAsync();
        }

        return new WalletRefundHoldResult { Applied = true, RefundId = refundId, RequestId = requestId };
    }

    public virtual Task<bool> HasWalletRefundAsync(Guid balanceTxnId)
    {
        return QueryAsync(async () =>
        {
            var dbContext = await _dbContextProvider.GetDbContextAsync();
            return await dbContext.WalletRefunds.AnyAsync(refund => refund.OriginalDebitTxnId == balanceTxnId);
        });
    }

    /// <summary>请求号是同一扣款下的幂等键，空值或超长会让两笔退款共用或写不进唯一索引。</summary>
    private static void AssertRequestId(string requestId)
    {
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 64 || requestId.Any(char.IsControl))
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount)
                .WithData("RequestId", "RequestId is required, at most 64 characters, without control characters.");
        }
    }

    private IUnitOfWork? BeginWalletRefundUnitOfWork()
    {
        var current = _unitOfWorkManager.Current;
        if (current?.Options.IsTransactional == true)
        {
            return null;
        }

        return _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
    }

    private static Task LockDebitAsync(CollectionDbContext dbContext, Guid debitId)
    {
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"ColBalanceTxns\" WHERE \"Id\" = {debitId} FOR UPDATE");
    }

    private async Task<bool> WalletLedgerEnforcedAsync()
    {
        return await _settingProvider.IsTrueAsync(CollectionSettings.WalletLedger.Enforce);
    }

    private async Task EnsureWalletMatchesLedgerAsync(Guid userId, CollectionCurrency currency)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var row = await dbContext.Balances.AsNoTracking()
            .FirstOrDefaultAsync(balance => balance.UserId == userId && balance.Currency == currency);
        var available = row?.Balance ?? 0m;
        var frozen = row?.Frozen ?? 0m;
        var ledgerAvailable = await _ledgerPoster.GetUserAvailableBalanceAsync(userId, currency);
        var ledgerFrozen = await _ledgerPoster.GetUserFrozenBalanceAsync(userId, currency);
        if (available != ledgerAvailable || frozen != ledgerFrozen)
        {
            throw new BusinessException(CollectionErrorCodes.WalletLedgerPending)
                .WithData("UserId", userId)
                .WithData("Currency", currency);
        }
    }
}
