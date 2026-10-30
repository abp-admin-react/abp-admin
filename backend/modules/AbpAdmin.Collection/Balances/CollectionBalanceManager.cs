using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Chain;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
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
/// 稳定币余额记账（计划第 3.1、5.5 节）。扣款刻意不新开工作单元：加入调用方的环境事务
///（订单的「扣款 + 标支付同事务」前提），锁住余额行直到外层提交；回冲自带 requiresNew
/// 事务，因为调用方（补状态作业、管理端）没有可加入的事务语义。
/// 金额一律 decimal，边界字符串转换在集成服务完成。
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

    public CollectionBalanceManager(
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ISettingProvider settingProvider,
        IDistributedEventBus distributedEventBus,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _dbContextProvider = dbContextProvider;
        _settingProvider = settingProvider;
        _distributedEventBus = distributedEventBus;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
    }

    // ── 扣款（计划第 3.1 节 DebitBalanceAsync / 第 5.5 节扣款 SQL）────────────────────────

    /// <summary>
    /// 扣 USDT / USDC。余额足够才减（行锁留到外层事务提交，命令超时读配置）；
    /// 同一业务单已有未回冲扣款时：金额币种相同返回原流水，否则冲突。
    /// 先插流水行（ON CONFLICT 探部分唯一索引）再减余额——重试路径不重复扣。
    /// </summary>
    public virtual async Task<Guid> DebitAsync(
        string businessType, Guid businessId, Guid userId, CollectionCurrency currency, decimal amount)
    {
        // 生产调用方（订单 PayWithBalanceAsync）持有事务性环境 UoW：加入它，不新开。
        // 环境事务必须真的是事务：非事务环境下两条语句各自自动提交，余额不足的失败路径会留下
        // 已提交的孤儿扣款行——补状态作业会把它当成已付款证据把订单标 Paid。
        // 没有环境事务时（直连场景/测试）自开事务性 UoW，保持「要么都在要么都不在」。
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
        if (amount <= 0)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Amount", amount);
        }

        var dbContext = await _dbContextProvider.GetDbContextAsync();
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

            if (winner.Currency != currency || winner.Amount != -amount)
            {
                throw new BusinessException(CollectionErrorCodes.DebitConflict)
                    .WithData("BusinessType", businessType)
                    .WithData("BusinessId", businessId);
            }

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
            // 余额不足（含无余额行）：当前余额写进日志便于客服分辨（计划第 5.5 节）
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

        return txnId;
    }

    // ── 回冲（计划第 3.1 节 ReverseDebitAsync）────────────────────────────────────────────

    /// <summary>
    /// 回冲该业务单未回冲的扣款。并发两次只加回一次余额（条件更新带 WHERE ReversedAt IS NULL，
    /// 0 行 = 已回冲，直接成功）；没有未回冲扣款时直接成功。回冲行流水用 IdempotencyKey 兜底。
    /// 调用方约束（订单不是 Paid/Fulfilled）由调用方负责——本模块不知道订单状态。
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

        var now = _clock.Now;
        var reversed = await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ColBalanceTxns\" SET \"ReversedAt\" = {now} WHERE \"Id\" = {debit.Id} AND \"ReversedAt\" IS NULL");
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

        await uow.CompleteAsync();
    }

    // ── 未回冲扣款查询（计划第 3.1 节 GetDebitAsync：补状态作业判断「已扣未标支付」）──────

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

    // ── 已入账充值清单（计划第 3.1 节 ListCreditedTxnsAsync：正向序号 + 7 天重扫）─────────

    /// <summary>
    /// 按单调序号列出已入账充值流水（Seq 升序、最多 maxCount），并重扫最近 7 天里
    /// 序号落在游标之前（曾被跳过）的充值行。两段按 Seq 天然不相交。
    /// Cursor = 本批最大序号；空批等于传入游标——被跳过的行只靠 7 天重扫补。
    /// </summary>
    public virtual Task<ListCreditedTxnsResult> ListCreditedAsync(long afterSeq, int maxCount)
    {
        return QueryAsync(async () =>
        {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var cutoff = _clock.Now.AddDays(-7);

        var forward = await dbContext.BalanceTxns.AsNoTracking()
            .Where(t => t.Action == BalanceActions.DepositCredit && t.Seq > afterSeq)
            .OrderBy(t => t.Seq)
            .Take(maxCount)
            .ToListAsync();

        var rescan = await dbContext.BalanceTxns.AsNoTracking()
            .Where(t => t.Action == BalanceActions.DepositCredit
                        && t.Seq <= afterSeq
                        && t.CreationTime >= cutoff)
            .OrderBy(t => t.Seq)
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
            // 单调：重扫-only 批的最大序号可能小于传入游标，不得回退（stub 与契约对齐）
            Cursor = merged.Count > 0 ? Math.Max(afterSeq, merged.Max(t => t.Seq)) : afterSeq,
        };
        });
    }

    // ── 充值入账（计划第 5.5 节；M5 归集确认后调用，测试与运维补账也走这里）──────────────

    /// <summary>
    /// 记一笔充值入账：IdempotencyKey 是进款单 id（重复提交只入账一次），
    /// 同事务写流水并加余额，BalanceCreditedEto 挂在环境工作单元完成之后发布。
    /// </summary>
    public virtual async Task<Guid> CreditAsync(
        Guid userId, CollectionCurrency currency, decimal amount, string idempotencyKey)
    {
        // 与扣款同一纪律（M5 的归集确认在事务里调它；测试与补账直连也安全）：
        // 非事务环境拒绝——插流水与加余额分两次自动提交，失败路径会让进款单幂等键
        // 永久吞掉一笔充值（重试只返回已存在，余额却从没加上）
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
        if (amount <= 0)
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

    // ── 内部 ─────────────────────────────────────────────────────────────────────────────


    /// <summary>余额行加钱（回冲/充值共用同一 upsert；带符号语义由调用方保证为正数）。</summary>
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

    /// <summary>流水行的 BalanceAfter 回填（扣/回冲/充值三处共用的第三条语句）。</summary>
    private static Task<int> SetBalanceAfterAsync(
        Microsoft.EntityFrameworkCore.DbContext dbContext, Guid txnId, decimal balanceAfter)
    {
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"ColBalanceTxns\" SET \"BalanceAfter\" = {balanceAfter} WHERE \"Id\" = {txnId}");
    }

    /// <summary>
    /// 环境工作单元若存在则必须是事务性的（fail-closed）：扣款/充值的多条语句要么整组提交、
    /// 要么整组回滚；非事务环境下加入等于各自自动提交，失败路径留下孤儿账。编程错误直接抛，
    /// 不降级——唯一合法调用方（订单 PayWithBalanceAsync）始终持事务性 requiresNew。
    /// </summary>
    private void AssertAmbientTransactionIfAny()
    {
        var current = _unitOfWorkManager.Current;
        if (current != null && !current.Options.IsTransactional)
        {
            throw new InvalidOperationException(
                "DebitBalance/Credit must run inside a transactional unit of work (or none, so one can be self-opened). " +
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
}
