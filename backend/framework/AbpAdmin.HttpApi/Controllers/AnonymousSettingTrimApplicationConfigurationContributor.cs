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
/// GetAsync 的对象初始化器完成之后统一执行（Setting 已填充，10.6.2 源码核实），此刻
/// Clear 即为最终响应值——本仓三个兄弟贡献者与 ABP 内置贡献者都只写 ExtraProperties、
/// 不写 Setting.Values，Clear 后无人再填充（顺序不敏感的安全位，注册在 HttpApi 层
/// 与宿主层均成立；由测试的过度裁剪守卫侧面钉住）。
/// 裁剪面刻意最小化：只清空 setting.values；localization（登录页文案）、features、
/// clock、currentTenant、扩展段（cookieConsent 等，由其它贡献者写入 ExtraProperties）
/// 全部保留。SPA 侧 settingValues 的三个消费点（IdleSessionWatcher、个人中心、租户管理）
/// 均以登录态为前置（2026-10-18 逐一核实，IdleSessionWatcher 有 currentUser.userid
/// 三重闸门），登录页不读 setting.values；登录完成后 SPA 重新拉取本端点，认证路径
/// 响应不变（setting 正常填充——上游对 IsAuthenticated=true 的请求本贡献者不触）。
/// 已知的匿名残余（有意留档，避免下次审计发现注释与现实对不上）：
/// /api/app/account-pro/is-self-registration-enabled（[AllowAnonymous]）仍原样返回
/// 「允许自建注册」这一个开关位——该端点无前端消费者（属历史端点），且登录页按同一
/// 设置条件渲染注册链接，这一比特本来就随页面泄露；与裁剪前 29 键全景不是一个量级。
/// 注册在 AbpAdminHttpApiModule（HttpApi 层、无宿主依赖，同 DataDictionaryTagTypes 惯例）：
/// 引用本层的任何组合根默认获得该防线，而不是每个宿主各自记得开启。
/// 由 AnonymousSettingTrimTests 三向钉死：注册在位（GitHub #19879：贡献者不自动发现）、
/// 匿名裁剪不过度（localization/扩展段保留）、认证路径不清空（ICurrentPrincipalAccessor.Change 对照）。
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
