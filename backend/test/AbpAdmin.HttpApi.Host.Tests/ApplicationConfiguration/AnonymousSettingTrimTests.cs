using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;
using Volo.Abp.Identity;
using Volo.Abp.Security.Claims;

namespace AbpAdmin.ApplicationConfiguration;

/// <summary>
/// 审计 F-E3 / 总结 #4 的落地面：匿名请求不得从 application-configuration 拿到
/// setting.values（Lockout/口令复杂度/Captcha/IdleTimeout 等登录防线画像）。
/// 三条断言各钉一个失配形态（对 <see cref="Controllers.AnonymousSettingTrimApplicationConfigurationContributor"/>）：
/// 1. 注册失配——贡献者没进 AbpApplicationConfigurationOptions.Contributors
///    （升级/重构静默丢失时先红；GitHub abpframework/abp#19879：贡献者不自动发现）；
/// 2. 过度裁剪——匿名分支清了 values 之外的东西（localization/兄弟贡献者扩展段
///    是登录页与 SPA 初始化的依赖，裁了它们登录页就白屏）；
/// 3. 认证向回归——守卫条件失去 !IsAuthenticated（或被扩大成无条件清空）时，
///    IdleSessionWatcher/租户连接串管理/个人中心的真源静默消失。
/// 认证态用 ICurrentPrincipalAccessor.Change 伪造（无 HTTP 管线时 ABP 的标准做法，
/// AsyncLocal 经 ExecutionContext 跨 await 生效；同款惯用法见 AccountProTwoFactorTests.AsUser）。
/// </summary>
public class AnonymousSettingTrimTests
{
    [RequiresHostDatabaseFact]
    public async Task Trim_Contributor_Must_Be_Registered()
    {
        var app = await HostTestBed.HostTask;

        var options = app.Services
            .GetRequiredService<IOptions<AbpApplicationConfigurationOptions>>().Value;

        options.Contributors
            .ShouldContain(c => c is Controllers.AnonymousSettingTrimApplicationConfigurationContributor,
                "匿名 setting.values 裁剪贡献者必须在 AbpApplicationConfigurationOptions.Contributors 里。" +
                "缺失说明注册被升级/重构静默丢掉——裁剪已失效（审计 F-E3 的防线画像重新对匿名方可见）。");
    }

    [RequiresHostDatabaseFact]
    public async Task Anonymous_Request_Should_Have_Empty_Setting_Values()
    {
        var app = await HostTestBed.HostTask;

        // 静态宿主没有 HTTP 管线与认证主体 → CurrentUser.IsAuthenticated = false，
        // 正是匿名分支（同一进程内直调服务与经控制器调用的差别只在传输层）。
        var service = app.Services.GetRequiredService<IAbpApplicationConfigurationAppService>();
        var configuration = await service.GetAsync(new ApplicationConfigurationRequestOptions());

        configuration.Setting.ShouldNotBeNull();
        configuration.Setting.Values.ShouldBeEmpty(
            "匿名请求的 application-configuration 不得携带 setting.values" +
            "（Lockout/口令策略/Captcha/IdleTimeout 等防线画像，审计 F-E3）。");

        // 裁剪面最小化契约（过度裁剪回归守卫）：只清 values，其余段必须原样保留。
        configuration.Localization.ShouldNotBeNull();
        configuration.Localization.Languages.ShouldNotBeEmpty(
            "匿名响应必须保留语言列表（登录页本地化初始化依赖）——裁剪面扩大到 localization 时这里先红。");
        configuration.ExtraProperties.ShouldContainKey("cookieConsent",
            "匿名响应必须保留 cookieConsent 扩展段（登录页 Cookie 横幅，CookieConsentApplicationConfigurationContributor 下发）。");
        configuration.ExtraProperties.ShouldContainKey("signalr",
            "匿名响应必须保留 signalr 扩展段（前端据 SignalR:Enabled 决定连接还是降级轮询）。");
    }

    [RequiresHostDatabaseFact]
    public async Task Authenticated_Request_Should_Keep_Setting_Values()
    {
        var app = await HostTestBed.HostTask;

        using var scope = app.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<IdentityUserManager>();
        var admin = await userManager.FindByNameAsync("admin");
        admin.ShouldNotBeNull("种子库必须有 admin——本测试是『认证路径不被裁剪』的对照半边。");

        var accessor = scope.ServiceProvider.GetRequiredService<ICurrentPrincipalAccessor>();
        var service = scope.ServiceProvider.GetRequiredService<IAbpApplicationConfigurationAppService>();

        using (accessor.Change(new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(AbpClaimTypes.UserId, admin.Id.ToString()),
            new Claim(AbpClaimTypes.UserName, admin.UserName),
        }, "Testing"))))
        {
            var configuration = await service.GetAsync(new ApplicationConfigurationRequestOptions());

            configuration.Setting.Values.ShouldNotBeEmpty(
                "认证请求的 setting.values 必须保留（IdleSessionWatcher、租户连接串管理、个人中心的真源）。" +
                "清空条件失去 !IsAuthenticated 守卫或被扩大时，这里先红，而不是等线上功能静默缺失。");
        }
    }
}
