using Volo.Abp;
using Volo.Abp.Identity;

namespace AbpAdmin.Identity;

/// <summary>
/// 双因素认证启用的领域前提（单一出处，自助端 AccountProAppService 与管理端
/// IdentityUserAdminAppService 共用）：启用前必须至少有一个已确认的联系方式
/// （邮箱或手机），否则登录流没有第二因子可投递。
/// 有意的不对称：禁用是安全方向的操作，不受此前提拦截——自助端禁用另需本人验码
/// （通道确认，属 AccountSecurity 流程），与本前提是两回事。
/// </summary>
public static class TwoFactorGuard
{
    public static void EnsureCanEnable(Volo.Abp.Identity.IdentityUser user)
    {
        if (!user.EmailConfirmed && !user.PhoneNumberConfirmed)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Account.TwoFactorRequiresConfirmedProvider);
        }
    }
}
