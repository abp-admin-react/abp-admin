using System;
using System.Threading.Tasks;
using EasyAbp.PaymentService.Prepayment.Accounts;
using EasyAbp.PaymentService.Prepayment.Transactions;
using EasyAbp.PaymentService.Prepayment.Transactions.Dtos;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Payments;

/// <summary>
/// 替换包 <see cref="TransactionAppService"/>：缺 AccountId（Guid.Empty）时包会对空 Id
/// GetAsync 账户，落成 EntityNotFound，再被客户错误管线展平成 Client:Unexpected/500。
/// 前置抛 <see cref="AbpAdminDomainErrorCodes.Payments.PrepaymentAccountIdRequired"/>
/// （公开目录 Kind=Validation → HTTP 400）。看他人流水仍走包内 Transaction.Manage（已钉 Host）。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(ITransactionAppService), typeof(TransactionAppService), IncludeSelf = true)]
public class AbpAdminTransactionAppService : TransactionAppService
{
    public AbpAdminTransactionAppService(
        IAccountRepository accountRepository,
        ITransactionRepository repository)
        : base(accountRepository, repository)
    {
    }

    public override async Task<PagedResultDto<TransactionDto>> GetListAsync(GetTransactionListInput input)
    {
        if (input.AccountId == Guid.Empty)
        {
            // 必须走已登记公开码：裸 AbpValidationException 会被客户错误管线展平成 Client:Unexpected/500
            throw new BusinessException(AbpAdminDomainErrorCodes.Payments.PrepaymentAccountIdRequired);
        }

        return await base.GetListAsync(input);
    }
}
