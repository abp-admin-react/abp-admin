using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Ledger.Integration.Contracts;

/// <summary>
/// Ledger 集成门面（定稿 §2）。进程内调用，加入调用方工作单元；不作为外部 HTTP API 暴露。
/// </summary>
[IntegrationService]
public interface ILedgerIntegrationService : IApplicationService
{
    /// <summary>
    /// 幂等过账。键 (Tenant,BizType,BizRefId,Phase,BatchKey)。
    /// 同键同内容 → 返回原 VoucherId；同键不同内容 → <see cref="FinanceErrorCodes.IdempotencyConflict"/>。
    /// </summary>
    Task<Guid> PostAsync(FinanceBizEvent evt, CancellationToken cancellationToken = default);

    /// <summary>
    /// 红冲（不删原分录，生成借贷互换的红字凭证，原凭证标 Reversed）。
    /// 已红冲过的重复调用返回同一张红字凭证 Id。
    /// </summary>
    Task<Guid> ReverseAsync(
        string bizType,
        string bizRefId,
        string phase,
        string? batchKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>子账户余额（按科目正常方向取正）；子账户不存在返回 0。</summary>
    Task<decimal> GetBalanceAsync(AccountRef account, string currency, CancellationToken cancellationToken = default);
}
