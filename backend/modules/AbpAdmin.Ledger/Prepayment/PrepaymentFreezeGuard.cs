using System.Threading.Tasks;
using AbpAdmin.Ledger.Integration.Contracts;
using AbpAdmin.Ledger.Settings;
using PrepaymentAccount = EasyAbp.PaymentService.Prepayment.Accounts.Account;
using EasyAbp.PaymentService.Prepayment.Transactions;
using EasyAbp.PaymentService.Prepayment.WithdrawalRecords;
using EasyAbp.PaymentService.Prepayment.WithdrawalRequests;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities.Events;
using Volo.Abp.EventBus;
using Volo.Abp.Settings;

namespace AbpAdmin.Ledger.Prepayment;

/// <summary>
/// Prepayment 停写（定稿 §7 / 开关 Finance.Prepayment.FreezeWrites）：开关为 true 时，
/// EasyAbp Prepayment 的账户/流水/提现单任何新增、修改、删除在本地实体事件里抛错——
/// 本地实体事件在提交前同一工作单元内触发，抛错即整个事务回滚，旧余额表不会再被写。
/// </summary>
public class PrepaymentFreezeGuard :
    ILocalEventHandler<EntityChangedEventData<PrepaymentAccount>>,
    ILocalEventHandler<EntityChangedEventData<Transaction>>,
    ILocalEventHandler<EntityChangedEventData<WithdrawalRecord>>,
    ILocalEventHandler<EntityChangedEventData<WithdrawalRequest>>,
    ITransientDependency
{
    private readonly ISettingProvider _settings;

    public PrepaymentFreezeGuard(ISettingProvider settings)
    {
        _settings = settings;
    }

    public Task HandleEventAsync(EntityChangedEventData<PrepaymentAccount> eventData) => GuardAsync(nameof(PrepaymentAccount));

    public Task HandleEventAsync(EntityChangedEventData<Transaction> eventData) => GuardAsync(nameof(Transaction));

    public Task HandleEventAsync(EntityChangedEventData<WithdrawalRecord> eventData) => GuardAsync(nameof(WithdrawalRecord));

    public Task HandleEventAsync(EntityChangedEventData<WithdrawalRequest> eventData) => GuardAsync(nameof(WithdrawalRequest));

    private async Task GuardAsync(string entity)
    {
        if (await _settings.IsTrueAsync(LedgerSettings.PrepaymentFreezeWrites))
        {
            throw new BusinessException(FinanceErrorCodes.PrepaymentWritesFrozen).WithData("Entity", entity);
        }
    }
}
