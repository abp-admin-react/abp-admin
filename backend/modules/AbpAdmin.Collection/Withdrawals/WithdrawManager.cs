using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Collection.Balances;
using AbpAdmin.Collection.Contracts;
using AbpAdmin.Collection.Data;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Collection.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Domain.Services;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.Settings;
using Volo.Abp.Timing;
using Volo.Abp.Uow;

namespace AbpAdmin.Collection.Withdrawals;

/// <summary>
/// 提现编排（提现 M2）：提交/撤单/审核/拒绝/失败退款。纪律——
/// 每个动作一个事务性 UoW（加入环境事务或自开），余额原语（CollectionBalanceManager）、
/// 状态迁移（CAS 条件 UPDATE）与账本过账（WithdrawLedgerPoster）三者同事务提交或回滚。
/// 当日额度在冻结锁内计数（并发提交串行化在余额行锁上，exchange 同款次序）。
/// 广播/卡单恢复/重试在 M3 的 WithdrawBroadcastWorker。
/// </summary>
public class WithdrawManager : DomainService
{
    /// <summary>全零黑洞地址（BNB/BSC 常见燃烧地址）。</summary>
    public const string BlackHoleAddress = "0x0000000000000000000000000000000000000000";

    private readonly CollectionBalanceManager _balances;
    private readonly WithdrawLedgerPoster _ledger;
    private readonly IDbContextProvider<CollectionDbContext> _dbContextProvider;
    private readonly ISettingProvider _settings;
    private readonly IConfiguration _configuration;
    private readonly IClock _clock;
    private readonly IUnitOfWorkManager _unitOfWorkManager;

    public WithdrawManager(
        CollectionBalanceManager balances,
        WithdrawLedgerPoster ledger,
        IDbContextProvider<CollectionDbContext> dbContextProvider,
        ISettingProvider settings,
        IConfiguration configuration,
        IClock clock,
        IUnitOfWorkManager unitOfWorkManager)
    {
        _balances = balances;
        _ledger = ledger;
        _dbContextProvider = dbContextProvider;
        _settings = settings;
        _configuration = configuration;
        _clock = clock;
        _unitOfWorkManager = unitOfWorkManager;
    }

    // ── 提交 ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 提交提现：校验 → 冻结总额（净额+手续费，锁余额行）→ 锁内查当日额度 → 落单 Pending → 过账 freeze。
    /// 任何一步失败整体回滚（不留孤儿冻结流水/订单行）。
    /// </summary>
    public virtual async Task<ColWithdrawOrder> SubmitAsync(
        Guid userId, CollectionCurrency currency, string toAddress, decimal amount)
    {
        AssertEnabled();
        await AssertNotHaltedAsync();
        if (currency != CollectionCurrency.Usdt && currency != CollectionCurrency.Usdc)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Currency", currency);
        }

        toAddress = ValidateAddress(toAddress);
        if (amount <= 0)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount).WithData("Amount", amount);
        }

        var minAmount = await GetSettingDecimalAsync(CollectionSettings.Withdraw.MinAmount, WithdrawDefaults.MinAmount);
        if (amount < minAmount)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawBelowMin)
                .WithData("Amount", CollectionAmounts.Format(amount))
                .WithData("Min", CollectionAmounts.Format(minAmount));
        }

        var feeRate = await GetSettingDecimalAsync(CollectionSettings.Withdraw.FeeRate, WithdrawDefaults.FeeRate);
        var fee = Truncate8(amount * feeRate);
        var actual = amount - fee;
        if (actual <= 0)
        {
            throw new BusinessException(CollectionErrorCodes.InvalidAmount)
                .WithData("Reason", "fee covers the whole amount");
        }

        AssertAmbientTransactionIfAny();
        if (_unitOfWorkManager.Current != null)
        {
            return await SubmitCoreAsync(userId, currency, toAddress, amount, fee, actual, feeRate);
        }

        using var uow = _unitOfWorkManager.Begin(isTransactional: true);
        var order = await SubmitCoreAsync(userId, currency, toAddress, amount, fee, actual, feeRate);
        await uow.CompleteAsync();
        return order;
    }

    private async Task<ColWithdrawOrder> SubmitCoreAsync(
        Guid userId, CollectionCurrency currency, string toAddress,
        decimal amount, decimal fee, decimal actual, decimal feeRate)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var orderId = GuidGenerator.Create();
        var now = _clock.Now;

        // ① 冻结总额：条件 UPDATE 拿余额行锁（并发提交在此串行化），可用不足抛 InsufficientBalance
        await _balances.FreezeAsync(userId, currency, amount, orderId);

        // ② 锁内查当日额度（UTC 日；撤单/拒绝释放额度，所以只排除这两个终态）
        var utcToday = now.Date;
        var todays = await dbContext.WithdrawOrders.AsNoTracking()
            .Where(o => o.UserId == userId && o.CreationTime >= utcToday
                        && o.Status != WithdrawStatus.Cancelled && o.Status != WithdrawStatus.Rejected)
            .ToListAsync();

        var maxCount = await GetSettingIntAsync(CollectionSettings.Withdraw.MaxDailyCount, WithdrawDefaults.MaxDailyCount);
        if (todays.Count >= maxCount)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawDailyCountExceeded)
                .WithData("Count", todays.Count).WithData("Max", maxCount);
        }

        var maxDaily = await GetDailyAmountLimitAsync();
        if (todays.Sum(o => o.Amount) + amount > maxDaily)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawDailyAmountExceeded)
                .WithData("Amount", CollectionAmounts.Format(amount))
                .WithData("Max", CollectionAmounts.Format(maxDaily));
        }

        // ③ 落单 Pending（余额流水与订单共用 orderId 作为幂等/关联键）
        var order = new ColWithdrawOrder(orderId)
        {
            UserId = userId,
            Currency = currency,
            NetworkName = AbpAdmin.Collection.Contracts.CollectionNetworkConsts.BscNetworkName,
            ToAddress = toAddress,
            Amount = amount,
            Fee = fee,
            ActualAmount = actual,
            FeeRate = feeRate,
            Status = WithdrawStatus.Pending,
            StatusChangedAt = now,
            CreationTime = now,
        };
        dbContext.WithdrawOrders.Add(order);
        await dbContext.SaveChangesAsync();

        // ④ 账本冻结（可用→冻结，同事务）
        await _ledger.PostFreezeAsync(order);
        return order;
    }

    /// <summary>当日总额度：留空 = 不限（decimal.MaxValue）。</summary>
    private async Task<decimal> GetDailyAmountLimitAsync()
    {
        var raw = await _settings.GetOrNullAsync(CollectionSettings.Withdraw.MaxDailyAmount);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return decimal.MaxValue;
        }

        return decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v
            : WithdrawDefaults.MaxDailyAmount;
    }

    // ── 撤单（仅 Pending，本人）──────────────────────────────────────────────────────────

    public virtual async Task CancelAsync(Guid userId, Guid orderId)
    {
        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var order = await FindOwnedAsync(userId, orderId);
        await CasTransitionAsync(order.Id, WithdrawStatus.Pending, WithdrawStatus.Cancelled,
            auditUserId: null, auditRemark: null, auditTime: null, failReason: "用户撤单");
        await _balances.UnfreezeAsync(order.UserId, order.Currency, order.Amount, order.Id);
        await _ledger.PostUnfreezeAsync(order);
        await uow.CompleteAsync();
    }

    // ── 审核（Pending → Processing；worker 小额自动审复用，auditUserId 可空）────────────

    public virtual async Task ApproveAsync(Guid orderId, Guid? auditUserId, string? remark)
    {
        await AssertNotHaltedAsync();

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var order = await FindAsync(orderId);
        var now = _clock.Now;
        await CasTransitionAsync(order.Id, WithdrawStatus.Pending, WithdrawStatus.Processing,
            auditUserId, remark, now, failReason: null);
        // 冻结出账（总额），随后净额转应付、手续费转收入（与余额两桶变化同账）
        await _balances.DeductFrozenAsync(order.UserId, order.Currency, order.Amount, order.Id);
        await _ledger.PostApproveAsync(order);
        await _ledger.PostFeeTakeAsync(order);
        await uow.CompleteAsync();
    }

    // ── 拒绝（Pending 拒绝 = 解冻；终态）─────────────────────────────────────────────────

    public virtual async Task RejectAsync(Guid orderId, Guid auditUserId, string remark)
    {
        if (string.IsNullOrWhiteSpace(remark))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawRemarkRequired);
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var order = await FindAsync(orderId);
        var now = _clock.Now;
        await CasTransitionAsync(order.Id, WithdrawStatus.Pending, WithdrawStatus.Rejected,
            auditUserId, remark, now, remark);
        await _balances.UnfreezeAsync(order.UserId, order.Currency, order.Amount, order.Id);
        await _ledger.PostUnfreezeAsync(order);
        await uow.CompleteAsync();
    }

    // ── 广播失败退款（BroadcastFailed → Rejected 终态；净额+手续费退回可用）──────────────

    public virtual async Task RefundAfterApproveAsync(Guid orderId, Guid auditUserId, string remark)
    {
        if (string.IsNullOrWhiteSpace(remark))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawRemarkRequired);
        }

        using var uow = _unitOfWorkManager.Begin(requiresNew: true, isTransactional: true);
        var order = await FindAsync(orderId);
        var now = _clock.Now;
        await CasTransitionAsync(order.Id, WithdrawStatus.BroadcastFailed, WithdrawStatus.Rejected,
            auditUserId, remark, now, remark);
        await _balances.RefundAsync(order.UserId, order.Currency, order.Amount, order.Id);
        await _ledger.PostRefundAsync(order);
        await _ledger.PostFeeRefundAsync(order);
        await uow.CompleteAsync();
    }

    // ── 查询 ─────────────────────────────────────────────────────────────────────────────

    public virtual async Task<ColWithdrawOrder> FindAsync(Guid orderId)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var order = await dbContext.WithdrawOrders.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == orderId);
        return order ?? throw new BusinessException(CollectionErrorCodes.WithdrawNotFound)
            .WithData("WithdrawOrderId", orderId);
    }

    // ── 内部 ─────────────────────────────────────────────────────────────────────────────

    /// <summary>功能总开关（宿主配置 Collection:Withdraw:Enabled，默认 false）。</summary>
    public virtual bool IsEnabled()
    {
        return bool.TryParse(_configuration[CollectionSettings.WithdrawConfig.Enabled], out var v) && v;
    }

    private void AssertEnabled()
    {
        if (!IsEnabled())
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawDisabled);
        }
    }

    private async Task AssertNotHaltedAsync()
    {
        var halt = await _settings.IsTrueAsync(CollectionSettings.Withdraw.Halt);
        if (halt)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawHalted);
        }
    }

    private async Task<ColWithdrawOrder> FindOwnedAsync(Guid userId, Guid orderId)
    {
        var order = await FindAsync(orderId);
        if (order.UserId != userId)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawNotOwner)
                .WithData("WithdrawOrderId", orderId);
        }

        return order;
    }

    /// <summary>状态迁移 CAS：0 行 = 已被并发迁移，抛 WithdrawInvalidStatus。审计/原因字段按终值直写。</summary>
    private async Task CasTransitionAsync(
        Guid orderId, WithdrawStatus from, WithdrawStatus to,
        Guid? auditUserId, string? auditRemark, DateTime? auditTime, string? failReason)
    {
        var dbContext = await _dbContextProvider.GetDbContextAsync();
        var now = _clock.Now;
        var rows = await dbContext.WithdrawOrders
            .Where(o => o.Id == orderId && o.Status == from)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, to)
                .SetProperty(o => o.StatusChangedAt, now)
                .SetProperty(o => o.AuditUserId, auditUserId)
                .SetProperty(o => o.AuditRemark, auditRemark)
                .SetProperty(o => o.AuditTime, auditTime)
                .SetProperty(o => o.FailReason, failReason));
        if (rows == 0)
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidStatus)
                .WithData("WithdrawOrderId", orderId)
                .WithData("From", from.ToString())
                .WithData("To", to.ToString());
        }
    }

    private static string ValidateAddress(string toAddress)
    {
        toAddress = (toAddress ?? string.Empty).Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(toAddress, "^0x[0-9a-fA-F]{40}$")
            || toAddress.Equals(BlackHoleAddress, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException(CollectionErrorCodes.WithdrawInvalidAddress)
                .WithData("ToAddress", toAddress);
        }

        return toAddress.ToLowerInvariant();
    }

    /// <summary>8 位小数截断（对齐 exchange 的手续费口径）。</summary>
    private static decimal Truncate8(decimal value)
    {
        return decimal.Truncate(value * 100_000_000m) / 100_000_000m;
    }

    private async Task<decimal> GetSettingDecimalAsync(string key, decimal fallback)
    {
        var raw = await _settings.GetOrNullAsync(key);
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Number,
                   System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0
            ? v
            : fallback;
    }

    private async Task<int> GetSettingIntAsync(string key, int fallback)
    {
        var raw = await _settings.GetOrNullAsync(key);
        return int.TryParse(raw, out var v) && v >= 0 ? v : fallback;
    }

    private void AssertAmbientTransactionIfAny()
    {
        var current = _unitOfWorkManager.Current;
        if (current != null && !current.Options.IsTransactional)
        {
            throw new InvalidOperationException(
                "WithdrawManager must run inside a transactional unit of work (or none, so one can be self-opened).");
        }
    }
}
