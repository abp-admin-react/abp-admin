using System;
using Volo.Abp;
using Volo.Abp.Data;
using Volo.Abp.Domain.Services;
using Volo.Abp.TenantManagement;

namespace AbpAdmin.Tenants;

/// <summary>
/// 租户激活状态校验（T2.8 SaaS Pro 缺口）。
/// 在登录流程（OpenIddict ProcessSignIn 事件，见 HttpApi.Host 的
/// TenantActivationOpenIddictServerHandler）中调用：
/// - <see cref="TenantActivationStateEnum.Passive"/> → 拒绝，提示租户已停用；
/// - <see cref="TenantActivationStateEnum.ActiveWithLimitedTime"/> 且 ActivationEndDate 已过期 → 拒绝，提示租户已到期；
/// - 其余放行（ActivationState 缺失按 Active 处理，与扩展属性默认值一致）。
/// </summary>
public class TenantActivationChecker : DomainService
{
    public virtual void Check(Tenant tenant)
    {
        var state = tenant.GetProperty<TenantActivationStateEnum>(
            AbpAdminTenantConsts.ActivationStatePropertyName,
            TenantActivationStateEnum.Active);

        switch (state)
        {
            case TenantActivationStateEnum.Passive:
                throw new BusinessException(AbpAdminDomainErrorCodes.Tenants.TenantIsPassive);
            case TenantActivationStateEnum.ActiveWithLimitedTime:
                var endDate = tenant.GetProperty<DateTime?>(
                    AbpAdminTenantConsts.ActivationEndDatePropertyName);
                if (endDate.HasValue && endDate.Value < Clock.Now)
                {
                    throw new BusinessException(AbpAdminDomainErrorCodes.Tenants.TenantActivationExpired);
                }

                break;
        }
    }
}
