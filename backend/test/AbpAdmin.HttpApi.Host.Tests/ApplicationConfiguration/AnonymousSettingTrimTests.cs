using System.Threading.Tasks;
using AbpAdmin.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;

namespace AbpAdmin.ApplicationConfiguration;

/// <summary>
/// 审计 F-E3 / 总结 #4 的落地面：匿名请求不得从 application-configuration 拿到
/// setting.values（Lockout/口令复杂度/Captcha/IdleTimeout 等登录防线画像）。
/// 两条断言对应 <see cref="Controllers.AnonymousSettingTrimApplicationConfigurationContributor"/>
/// 的两种失配形态：
/// 1. 注册失配——贡献者没进 AbpApplicationConfigurationOptions.Contributors
///    （升级/重构静默丢失时先红，而不是等裁剪在线上消失才发现）；
/// 2. 行为失配——贡献者在位但匿名分支没裁干净（上游若把 Setting 填充挪到贡献者之后，
///    这里同样会红——顺序契约是 10.6.2 源码核实过的，见贡献者注释）。
/// 认证路径（values 正常填充）无法在无 HTTP 管线的静态宿主里伪造登录态，
/// 由靶场冒烟覆盖（fin-ops 换票后拉取本端点比对，见 FIX_VERIFICATION 第二轮）。
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
    }
}
