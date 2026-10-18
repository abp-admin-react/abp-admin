using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.Users;

namespace AbpAdmin.Controllers;

/// <summary>
/// 匿名请求裁剪 application-configuration 的 <c>setting.values</c>（2026-10-18 安全审计
/// F-E3 / 总结 #4）。背景：匿名 GET /api/abp/application-configuration 返回 200 / 355066 字节，
/// 逐键给出 Lockout 5 次/300 秒、口令最低 6 位且复杂度四项全关、Captcha.Enabled=false、
/// IdleSessionTimeoutMinutes=0、允许自建注册——等于把登录防线画像免费发给攻击者做
/// 口令喷洒/撞库前的侦察。官方口径（abp.io/support/questions/9877）：端点匿名可达是
/// 设计使然（SPA 登录页初始化必需，"You can't remove it"），收紧的正解是裁剪响应。
/// 实现取贡献者而非替换 IAbpApplicationConfigurationAppService：上游服务是显式 18 参
/// 构造（10.6.2 源码核实），子类化要把全部依赖原样转发，升级脆弱；而贡献者在上游
/// GetAsync 的对象初始化器完成之后统一执行（Setting 已填充、ExtraProperties 为空，
/// 10.6.2 源码核实），此刻 Clear 即为最终响应值。
/// 裁剪面刻意最小化：只清空 setting.values；localization（登录页文案）、features、
/// clock、currentTenant、扩展段（cookieConsent 等，由其它贡献者写入 ExtraProperties）
/// 全部保留。SPA 侧 settingValues 的三个消费点（IdleSessionWatcher、个人中心、租户管理）
/// 均以登录态为前置（2026-10-18 逐一核实，IdleSessionWatcher 有 currentUser.userid
/// 三重闸门），登录页不读 setting.values；登录完成后 SPA 重新拉取本端点，认证路径
/// 响应不变（setting 正常填充——上游对 IsAuthenticated=true 的请求本贡献者不触）。
/// 由 AnonymousSettingTrimTests 双向钉死：贡献者必须在 AbpApplicationConfigurationOptions
/// 注册表里（升级后注册静默丢失时先红），且匿名结果 values 必须为空。
/// </summary>
public class AnonymousSettingTrimApplicationConfigurationContributor : IApplicationConfigurationContributor
{
    public Task ContributeAsync(ApplicationConfigurationContributorContext context)
    {
        var currentUser = context.ServiceProvider.GetRequiredService<ICurrentUser>();

        if (!currentUser.IsAuthenticated)
        {
            context.ApplicationConfiguration.Setting?.Values.Clear();
        }

        return Task.CompletedTask;
    }
}
