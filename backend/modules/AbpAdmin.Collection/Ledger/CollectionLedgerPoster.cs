using System;
using System.Threading.Tasks;
using AbpAdmin.Collection.Entities;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp.Domain.Services;

namespace AbpAdmin.Collection.Ledger;

/// <summary>
/// Collection→总账的过账薄封装（充值入账/手续费、提现全状态机、存量开账、币种码收口）：FinanceBizEvent 构造收口在此（From/To、金额口径、幂等 BizRefId），
/// 业务管理器只挑 phase。所有过账加入调用方事务（ILedgerIntegrationService 进程内实现），
/// 幂等由引擎的 (BizType, BizRefId, Phase) 唯一键承担——BizRefId 统一 = 提现单/进款单 Id 的 "D" 格式。
/// 金额口径：freeze/unfreeze = Amount+Fee（冻结总额）；approve/payout/refund = ActualAmount（净额）；
/// fee-take/fee-refund = Fee。
/// </summary>
public class CollectionLedgerPoster : DomainService
{
    private readonly ILedgerIntegrationService _ledger;

    public CollectionLedgerPoster(ILedgerIntegrationService ledger)
    {
        _ledger = ledger;
    }

    /// <summary>
    /// 账本币种码（大写）：CollectionCurrency.ToString() 给枚举名 "Usdt"，
    /// 账本子账户与查询两侧都必须走这里，大小写错位会让写入与查询互不可见。
    /// </summary>
    public static string CurrencyCode(Contracts.CollectionCurrency currency)
    {
        return currency == Contracts.CollectionCurrency.Usdc ? "USDC" : "USDT";
    }

    /// <summary>
    /// 充值入账净额（归集确认路径调用，与 ColBalance CreditAsync 同事务）。
    /// 有手续费时另过 <see cref="PostDepositFeeAsync"/>（借 2201 用户可用 / 贷 6003 手续费收入）。
    /// </summary>
    public virtual Task<Guid> PostDepositCreditAsync(Guid depositOrderId, Guid userId, Contracts.CollectionCurrency currency, decimal amount)
    {
        return _ledger.PostAsync(new FinanceBizEvent(
            FinanceBizTypes.Deposit, depositOrderId.ToString("D"), FinanceBizTypes.Phases.Pay,
            CurrencyCode(currency), amount,
            UserId: userId,
            From: Ref(FinanceAccountCodes.OnChainAsset),
            To: Ref(FinanceAccountCodes.UserAvailable, FinOwnerType.User, userId.ToString()),
            Summary: $"充值入账 {amount:0.########} {currency}"));
    }

    /// <summary>充值手续费过账（Fee = 0 跳过；幂等键 = 同单 phase=fee）。</summary>
    public virtual async Task<Guid?> PostDepositFeeAsync(Guid depositOrderId, Guid userId, Contracts.CollectionCurrency currency, decimal fee)
    {
        if (fee == 0m)
        {
            return null;
        }

        return await _ledger.PostAsync(new FinanceBizEvent(
            FinanceBizTypes.Deposit, depositOrderId.ToString("D"), FinanceBizTypes.Phases.Fee,
            CurrencyCode(currency), fee,
            UserId: userId,
            From: Ref(FinanceAccountCodes.UserAvailable, FinOwnerType.User, userId.ToString()),
            To: Ref(FinanceAccountCodes.WithdrawFeeIncome),
            Summary: $"充值手续费 {fee:0.########} {CurrencyCode(currency)}"));
    }

    /// <summary>
    /// 充值手续费快照（建单时调用，与 <see cref="ResolveCreditAmount"/> 是费率策略的两半——
    /// 同文件共置：费数学分裂两处曾险些造成双扣费）。
    /// fee = truncate8(毛额 × 费率)，actual = 毛额 − fee。费率 0 = 不收费。
    /// 费率钳制 [0,1]：≤0 不收费；≥1 视作全额费（actual≤0 由建单路径转终态）。
    /// 不钳制时 DB 直改的荒谬费率（如 1e19）会让 value×rate×1e8 溢出抛 OverflowException，
    /// 中断该币整轮扫链且游标停摆。null 毛额（超精度路径）安全返回 0。
    /// </summary>
    internal static (decimal FeeRate, decimal Fee, decimal Actual) SnapshotDepositFee(decimal feeRate, decimal? gross)
    {
        if (gross is not { } value)
        {
            return (feeRate, 0m, 0m);
        }

        var rate = Math.Clamp(feeRate, 0m, 1m);
        var fee = rate <= 0 ? 0m : decimal.Truncate(value * rate * 100_000_000m) / 100_000_000m;
        return (feeRate, fee, value - fee);
    }

    /// <summary>入账净额解析：优先快照 ActualAmount；历史单（null/0 且无手续费）回退毛额（线上同款兼容）。</summary>
    public static decimal ResolveCreditAmount(decimal? amount, decimal? actualAmount, decimal? fee)
    {
        if (actualAmount is { } actual && actual > 0m)
        {
            return actual;
        }

        // 「有 Fee 无快照净额」（仅手工改库可达）：重建 amount−fee 而非返回 0——0 会让
        // CreditAsync 抛 InvalidAmount 回滚整个归集确认 UoW，任务永久卡 Sweeping 并
        // 阻塞同任务其它进款单（管道不可达，但 0 值已被纯函数测试锁成契约）
        if (amount is not { } gross)
        {
            return 0m;
        }

        return fee is > 0m ? gross - fee.Value : gross;
    }

    /// <summary>提现提交冻结（可用→冻结，金额 = 总额）。</summary>
    public virtual Task<Guid> PostFreezeAsync(ColWithdrawOrder order)
    {
        return _ledger.PostAsync(Event(order, FinanceBizTypes.Phases.Freeze, order.Amount,
            From: Ref(FinanceAccountCodes.UserAvailable, FinOwnerType.User, order.UserId.ToString()),
            To: Ref(FinanceAccountCodes.UserFrozen, FinOwnerType.User, order.UserId.ToString()),
            summary: $"提现冻结 {order.Amount:0.########} {order.Currency}"));
    }

    /// <summary>撤单/拒绝解冻（冻结→可用，金额 = 总额；approve 前的拒绝路径）。</summary>
    public virtual Task<Guid> PostUnfreezeAsync(ColWithdrawOrder order)
    {
        return _ledger.PostAsync(Event(order, FinanceBizTypes.Phases.Unfreeze, order.Amount,
            From: Ref(FinanceAccountCodes.UserFrozen, FinOwnerType.User, order.UserId.ToString()),
            To: Ref(FinanceAccountCodes.UserAvailable, FinOwnerType.User, order.UserId.ToString()),
            summary: "提现解冻"));
    }

    /// <summary>审核通过（冻结→应付，金额 = 净额）。</summary>
    public virtual Task<Guid> PostApproveAsync(ColWithdrawOrder order)
    {
        return _ledger.PostAsync(Event(order, FinanceBizTypes.Phases.Approve, order.ActualAmount,
            From: Ref(FinanceAccountCodes.UserFrozen, FinOwnerType.User, order.UserId.ToString()),
            To: Ref(FinanceAccountCodes.AccountsPayable),
            summary: $"提现审核通过，应付 {order.ActualAmount:0.########} {order.Currency}"));
    }

    /// <summary>手续费实现（冻结→收入，金额 = 手续费）。Fee = 0 时跳过（不落零额凭证）。</summary>
    public virtual async Task<Guid?> PostFeeTakeAsync(ColWithdrawOrder order)
    {
        if (order.Fee == 0m)
        {
            return null;
        }

        return await _ledger.PostAsync(Event(order, FinanceBizTypes.Phases.FeeTake, order.Fee,
            From: Ref(FinanceAccountCodes.UserFrozen, FinOwnerType.User, order.UserId.ToString()),
            To: Ref(FinanceAccountCodes.WithdrawFeeIncome),
            summary: $"提现手续费 {order.Fee:0.########} {order.Currency}"));
    }

    /// <summary>打款完成（应付→链上资产，金额 = 净额）。带链上哈希进摘要，凭证即对账线索。</summary>
    public virtual Task<Guid> PostPayoutAsync(ColWithdrawOrder order, string txHash)
    {
        return _ledger.PostAsync(Event(order, FinanceBizTypes.Phases.Payout, order.ActualAmount,
            From: Ref(FinanceAccountCodes.AccountsPayable),
            To: Ref(FinanceAccountCodes.OnChainAsset),
            summary: $"提现打款完成 {txHash}"));
    }

    /// <summary>广播失败退款（应付→可用，金额 = 净额）。</summary>
    public virtual Task<Guid> PostRefundAsync(ColWithdrawOrder order)
    {
        return _ledger.PostAsync(Event(order, FinanceBizTypes.Phases.Refund, order.ActualAmount,
            From: Ref(FinanceAccountCodes.AccountsPayable),
            To: Ref(FinanceAccountCodes.UserAvailable, FinOwnerType.User, order.UserId.ToString()),
            summary: "提现失败退款"));
    }

    /// <summary>广播失败退手续费（收入→可用，金额 = 手续费）。Fee = 0 时跳过。</summary>
    public virtual async Task<Guid?> PostFeeRefundAsync(ColWithdrawOrder order)
    {
        if (order.Fee == 0m)
        {
            return null;
        }

        return await _ledger.PostAsync(Event(order, FinanceBizTypes.Phases.FeeRefund, order.Fee,
            From: Ref(FinanceAccountCodes.WithdrawFeeIncome),
            To: Ref(FinanceAccountCodes.UserAvailable, FinOwnerType.User, order.UserId.ToString()),
            summary: "提现退手续费"));
    }

    /// <summary>存量余额开账回填（BizRefId = opening:{userId}:{currency}，幂等防重跑）。
    /// 币种收枚举：调用方曾传 Currency.ToString()（"Usdt"）与提现侧 "USDT" 错位成两个子户，
    ///——币种码唯一入口是 <see cref="CurrencyCode"/>。</summary>
    public virtual Task<Guid> PostOpeningAsync(Guid userId, Contracts.CollectionCurrency currency, decimal amount)
    {
        var code = CurrencyCode(currency);
        return _ledger.PostAsync(new FinanceBizEvent(
            FinanceBizTypes.Wallet, $"opening:{userId:N}:{code}", FinanceBizTypes.Phases.Post,
            code, amount,
            UserId: userId,
            From: Ref(FinanceAccountCodes.OnChainAsset),
            To: Ref(FinanceAccountCodes.UserAvailable, FinOwnerType.User, userId.ToString()),
            Summary: $"存量余额开账 {amount:0.########} {currency}"));
    }

    /// <summary>查用户可用科目的账本投影余额（开账回填的差额计算用；币种同样收枚举收口）。</summary>
    public virtual Task<decimal> GetUserAvailableBalanceAsync(Guid userId, Contracts.CollectionCurrency currency)
    {
        return _ledger.GetBalanceAsync(
            Ref(FinanceAccountCodes.UserAvailable, FinOwnerType.User, userId.ToString()), CurrencyCode(currency));
    }

    private static FinanceBizEvent Event(
        ColWithdrawOrder order, string phase, decimal amount,
        AccountRef? From = null, AccountRef? To = null, string? summary = null)
    {
        return new FinanceBizEvent(
            FinanceBizTypes.Withdraw, order.Id.ToString("D"), phase,
            CurrencyCode(order.Currency), amount,
            UserId: order.UserId,
            From: From,
            To: To,
            Summary: summary);
    }

    private static AccountRef Ref(string code, FinOwnerType? owner = null, string? ownerId = null)
    {
        return new AccountRef(code, owner, ownerId);
    }
}
