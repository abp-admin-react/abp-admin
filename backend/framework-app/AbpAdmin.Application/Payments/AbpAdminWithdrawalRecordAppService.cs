using EasyAbp.PaymentService.Prepayment.WithdrawalRecords;
using Volo.Abp.DependencyInjection;

namespace AbpAdmin.Payments;

/// <summary>
/// 替换包 <see cref="WithdrawalRecordAppService"/>：包 GetList 无归属过滤，持 Default
/// 即可枚举租户内全部提现记录。将列表策略抬到 Manage（已钉 Host），自助 by-id 仍用 Default。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IWithdrawalRecordAppService), typeof(WithdrawalRecordAppService), IncludeSelf = true)]
public class AbpAdminWithdrawalRecordAppService : WithdrawalRecordAppService
{
    public AbpAdminWithdrawalRecordAppService(IWithdrawalRecordRepository repository)
        : base(repository)
    {
        GetListPolicyName = "EasyAbp.PaymentService.Prepayment.WithdrawalRecord.Manage";
    }
}
