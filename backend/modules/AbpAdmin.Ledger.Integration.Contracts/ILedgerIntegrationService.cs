using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.Application.Services;

namespace AbpAdmin.Ledger.Integration.Contracts;

/// <summary>
/// Ledger 集成门面（定稿 §2）。进程内调用，加入调用方工作单元；不作为外部 HTTP API 暴露。
///
/// 【接线状态（OCR design 复核注记）】本契约当前消费者仅 demo 通道与测试；真实业务入账
/// （TokenGateway 充值/消费、Ordering 退款、Collection 归集）尚未接线，预期在路线 C 的 F3
/// 阶段落地——届时 TokenGateway 承担「Tg 账 vs Fin 账」的跨账对账口径定义（谁定期跑、以哪本
/// 账为基准、差异怎么挂账）。若 F3 最终不消费本契约，应折回 Ledger 内部并删除本工程，避免
/// 长期无人压测的契约腐化。
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

    /// <summary>
    /// 子账户余额（按科目正常方向取正）；子账户不存在返回 0。
    /// 注意：科目编码未知/停用是另一条路径——抛 <see cref="FinanceErrorCodes.AccountNotFound"/>（HTTP 口径 400），
    /// 调用方需与「余额为 0」区分处理。
    /// </summary>
    Task<decimal> GetBalanceAsync(AccountRef account, string currency, CancellationToken cancellationToken = default);
}
