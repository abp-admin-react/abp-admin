using AbpAdmin.Account;
using AbpAdmin.ScheduledJobs;
using AbpAdmin.TextTemplates;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Generic;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using Volo.Abp.Modularity;
using Volo.Abp.TextTemplating;

namespace AbpAdmin;

[DependsOn(
    typeof(AbpAdminApplicationModule),
    typeof(AbpAdminDomainTestModule)
)]
public class AbpAdminApplicationTestModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        // LinkAccounts/Magic Link：PasswordlessMagicLinkUrlBuilder 读 App:SpaUrl（回落 App:SelfUrl）
        // 构造邮件里的登录链接，缺配置会直接抛 AbpException；测试给一组内存配置
        //（.local 保留域，与下方 Authority 同策略——永不真实解析）。
        context.Services.Replace(ServiceDescriptor.Singleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["App:SelfUrl"] = "https://test.local",
                ["App:SpaUrl"] = "http://test.localhost:8000",
                ["AuthServer:Authority"] = "https://test.local"
            }).Build()));

        // round4：ConfirmEmail/ConfirmPhone/无密码走通需要 Identity 的 token providers
        // （GenerateEmailConfirmationTokenAsync / GenerateChangePhoneNumberTokenAsync），
        // ABP 域模块不注册默认 providers，缺了会抛 "No IUserTwoFactorTokenProvider named 'Default'"。
        // DataProtectorTokenProvider 还依赖 IDataProtectionProvider——测试基座没有 Web 宿主的
        // DataProtection 栈，补一个进程内易失 provider（密钥不落盘，测试进程生命周期内自洽）。
        context.Services.AddSingleton<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(
            new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider());
        new IdentityBuilder(typeof(Volo.Abp.Identity.IdentityUser), typeof(Volo.Abp.Identity.IdentityRole), context.Services)
            .AddDefaultTokenProviders();

        // T2.3：测试专用模板定义（StoredTemplateContentContributorTests 的被测模板）
        Configure<AbpTextTemplatingOptions>(options =>
        {
            options.DefinitionProviders.Add<TestTemplateDefinitionProvider>();
        });

        // T2.7：防枚举/无密码登录测试需要断言「是否调用了邮件服务」，
        // 用录制型替身替换（Domain 层 DEBUG 下注册的 NullEmailSender 之后生效）。
        context.Services.Replace(ServiceDescriptor.Singleton<IEmailSender, RecordingEmailSender>());

        // IP 归属地：真实 IpRegionSearcher 依赖 xdb 文件（测试环境无），换固定映射替身。
        context.Services.Replace(ServiceDescriptor.Singleton<IpRegions.IIpRegionSearcher, IpRegions.StubIpRegionSearcher>());

        // T3.3：真实 ScheduledJobScheduler 依赖 Quartz 的 IScheduler（测试环境没有），
        // 且默认动态 worker manager 收到 cron 会抛 AbpException。
        // 换成录制型替身，测试断言 AppService 与调度器的交互。
        context.Services.Replace(ServiceDescriptor.Singleton<IScheduledJobScheduler, RecordingScheduledJobScheduler>());

        // T3.5：阿里云/腾讯云请求器换成录制替身，避免真实外呼；测试断言消息映射与凭据读取。
        // 必须排在 Domain 层 SettingBasedAliyunApiRequester（ReplaceServices）之后——本模块 ConfigureServices 更晚。
        context.Services.Replace(ServiceDescriptor.Singleton<EasyAbp.Abp.Aliyun.Common.IAliyunApiRequester, Notifications.RecordingAliyunApiRequester>());
        context.Services.Replace(ServiceDescriptor.Singleton<EasyAbp.Abp.TencentCloud.Common.Requester.ITencentCloudApiRequester, Notifications.RecordingTencentCloudApiRequester>());

        // HttpAgent 声明式客户端（IConnectTokenApi / ITurnstileSiteVerifyApi）的出站 HTTP 替身：
        // 挂各命名 HttpClient 的主处理器位（本模块晚于生产模块注册，最后生效），不真实外呼。
        // 测试配置 AuthServer:Authority 指向 .invalid 保留域（RFC 2606，保证不解析、无外呼），
        // 即使某个未挂替身的测试套件意外触达 token 交换，也只是即时 DNS 失败而非真实出网。
        context.Services.AddSingleton<HttpStubs.RecordingConnectTokenHandler>();
        context.Services.AddSingleton<HttpStubs.RecordingTurnstileHandler>();
        context.Services.AddHttpClient(AbpAdminApplicationModule.AuthServerTokenExchangeHttpClient)
            .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<HttpStubs.RecordingConnectTokenHandler>());
        context.Services.AddHttpClient(Captcha.TurnstileHttpClients.SiteVerify)
            .ConfigurePrimaryHttpMessageHandler(sp => sp.GetRequiredService<HttpStubs.RecordingTurnstileHandler>());
    }
}
