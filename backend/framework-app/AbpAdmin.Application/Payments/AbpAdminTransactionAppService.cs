using System;
using System.Threading.Tasks;
using EasyAbp.PaymentService.Prepayment.Accounts;
using EasyAbp.PaymentService.Prepayment.Transactions;
using EasyAbp.PaymentService.Prepayment.Transactions.Dtos;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Entities;

namespace AbpAdmin.Payments;

/// <summary>
/// 替换包 <see cref="TransactionAppService"/>：空 AccountId →
/// <see cref="AbpAdminDomainErrorCodes.Payments.PrepaymentAccountIdRequired"/>（Validation/400）；
/// 缺失账户的 EntityNotFound → <see cref="AbpAdminDomainErrorCodes.Payments.PrepaymentAccountNotFound"/>（NotFound/404）。
/// 二者避免包 GetAsync 落成 Client:Unexpected/500。看他人流水仍走 Transaction.Manage（已钉 Host）。
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
            throw new BusinessException(AbpAdminDomainErrorCodes.Payments.PrepaymentAccountIdRequired);
        }

        try
        {
            return await base.GetListAsync(input);
        }
        catch (EntityNotFoundException)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Payments.PrepaymentAccountNotFound)
                .WithData("AccountId", input.AccountId);
        }
    }
}
