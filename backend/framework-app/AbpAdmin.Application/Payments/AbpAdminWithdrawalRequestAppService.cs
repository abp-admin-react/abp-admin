using System.Threading.Tasks;
using EasyAbp.PaymentService.Prepayment.Accounts;
using EasyAbp.PaymentService.Prepayment.WithdrawalRequests;
using EasyAbp.PaymentService.Prepayment.WithdrawalRequests.Dtos;
using Volo.Abp.Application.Dtos;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Payments;

/// <summary>
/// 替换包 <see cref="WithdrawalRequestAppService"/>：无 AccountId/AccountUserId 过滤的
/// GetList 是租户内全量枚举，包实现不要求 Manage。
/// 与「看他人」同门槛强制 <see cref="ManagePermissionName"/>（已钉 Host ⇒ 租户侧 403）；
/// 带自有 AccountId/AccountUserId 过滤的自助列表仍走 Default。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IWithdrawalRequestAppService), typeof(WithdrawalRequestAppService), IncludeSelf = true)]
public class AbpAdminWithdrawalRequestAppService : WithdrawalRequestAppService
{
    public const string ManagePermissionName =
        "EasyAbp.PaymentService.Prepayment.WithdrawalRequest.Manage";

    public AbpAdminWithdrawalRequestAppService(
        IAccountRepository accountRepository,
        IAccountWithdrawalManager accountWithdrawalManager,
        IWithdrawalRequestRepository repository)
        : base(accountRepository, accountWithdrawalManager, repository)
    {
    }

    public override async Task<PagedResultDto<WithdrawalRequestDto>> GetListAsync(
        GetWithdrawalRequestListInput input)
    {
        if (!input.AccountId.HasValue && !input.AccountUserId.HasValue)
        {
            await CheckPolicyAsync(ManagePermissionName);
        }

        return await base.GetListAsync(input);
    }
}
