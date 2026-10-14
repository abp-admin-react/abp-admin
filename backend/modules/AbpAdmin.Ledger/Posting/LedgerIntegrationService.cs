using System;
using System.Threading;
using System.Threading.Tasks;
using AbpAdmin.Ledger.Integration.Contracts;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Uow;

namespace AbpAdmin.Ledger.Posting;

/// <summary>ILedgerIntegrationService 实现：进程内门面，加入调用方 UoW（无外层时自开事务）。</summary>
[RemoteService(IsEnabled = false)]
/// <summary>
/// 进程内 ILedgerIntegrationService 实现：业务模块过账入口。
/// 委托 <see cref="LedgerPostingEngine"/>；幂等键 (BizType,BizRefId,Phase,BatchKey)；
/// 时间与余额均按 UTC / 投影表。真实业务勿走 FinDemoEvent（仅 demo.*）。
/// </summary>
public class LedgerIntegrationService : ApplicationService, ILedgerIntegrationService
{
    private readonly LedgerPostingEngine _engine;

    public LedgerIntegrationService(LedgerPostingEngine engine)
    {
        _engine = engine;
    }

    [UnitOfWork(isTransactional: true)]
    public virtual Task<Guid> PostAsync(FinanceBizEvent evt, CancellationToken cancellationToken = default) =>
        _engine.PostAsync(evt);

    [UnitOfWork(isTransactional: true)]
    public virtual Task<Guid> ReverseAsync(string bizType, string bizRefId, string phase, string? batchKey = null,
        CancellationToken cancellationToken = default) =>
        _engine.ReverseAsync(bizType, bizRefId, phase, batchKey);

    public virtual Task<decimal> GetBalanceAsync(AccountRef account, string currency, CancellationToken cancellationToken = default) =>
        _engine.GetBalanceAsync(account, currency);
}
