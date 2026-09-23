using System;
using System.Threading.Tasks;

namespace AbpAdmin.Account;

/// <summary>
/// 防邮箱/账号枚举的耗时对齐（审查轮从两个 AppService 的私有副本收拢）：
/// 对"无效输入"路径模拟正常路径耗时，避免用时序差异探测账号是否存在。
/// 延迟区间见 <see cref="AbpAdminAccountConsts"/>。
/// </summary>
public static class AccountAntiEnumeration
{
    public static Task DelayAsync()
    {
        return Task.Delay(Random.Shared.Next(
            AbpAdminAccountConsts.AntiEnumerationDelayMinMs,
            AbpAdminAccountConsts.AntiEnumerationDelayMaxMs));
    }
}
