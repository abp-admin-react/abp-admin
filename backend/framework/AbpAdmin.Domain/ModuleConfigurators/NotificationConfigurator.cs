using AbpAdmin.Notifications;
using EasyAbp.NotificationService;
using EasyAbp.NotificationService.Options;
using EasyAbp.NotificationService.Provider.Mailing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Volo.Abp.Emailing;
using Volo.Abp.MailKit;

namespace AbpAdmin.ModuleConfigurators;

/// <summary>
/// 通知渠道（站内信/邮件/短信）配置（自 AbpAdminDomainModule 拆出，注册等价搬移）。
/// </summary>
internal static class NotificationConfigurator
{
    public static void ConfigureNotificationChannels(this IServiceCollection services, IConfiguration configuration)
    {
        // ========== T3.5 通知服务 ==========

        // 站内信渠道注册（复用模块的记录/状态/重试机制，自研 manager 只做 SignalR 推送）
        services.Configure<NotificationServiceOptions>(options =>
        {
            options.Providers.AddProvider(new NotificationServiceProviderConfiguration(
                Notifications.InAppNotificationConsts.NotificationMethod, typeof(Notifications.InAppNotificationManager)));
        });

        // 邮件渠道：MailKit。SecureSocketOption 只有这一个成员（10.6.0 反编译核实）；
        // 为 null 时 MailKit 用 Auto。465 端口配 SslOnConnect，587 配 StartTls。
        services.Configure<AbpMailKitOptions>(options =>
        {
            options.SecureSocketOption = configuration.GetValue<MailKit.Security.SecureSocketOptions?>("Mailing:SecureSocketOption");
        });

        // 邮件发送开关：运行期配置（替代原编译期 DEBUG 开关——Debug 构建部署到测试环境会
        // 静默吞掉所有邮件且无任何日志）。默认真发；显式设 false 时用 NullEmailSender 并打 Warning。
        // MailKitSmtpEmailSender 带 ReplaceServices（10.6.0 反编译核实），这里再 Replace 一次覆盖它。
        if (!configuration.GetValue<bool>("Mailing:UseRealSender", true))
        {
            // 降级发送器写成 .eml 文件（App_Data/outgoing-emails），本地 E2E 可观测邮件内容，
            // 替代 NullEmailSender 的静默丢弃
            services.Replace(ServiceDescriptor.Singleton<IEmailSender, Mailing.FileEmlEmailSender>());
            services.GetInitLogger<AbpAdminDomainModule>().LogWarning(
                "Mailing:UseRealSender = false：IEmailSender 已替换为 FileEmlEmailSender，邮件写入 App_Data/outgoing-emails。");
        }
    }

    public static void ConfigureSmsResolvers(this IServiceCollection services)
    {
        // 短信渠道：ISmsSender 唯一入口 = SmsSenderResolver（按 AbpAdmin.Sms.Provider 设置项分发）。
        // RemoveAll 清掉框架 NullSmsSender（TryRegister）等已有 ISmsSender 注册；
        // 本程序集的 *SmsSender 类均已用 [ExposeServices(自身)] 收窄，不会命中
        // "类名以 SmsSender 结尾 → 默认接口 ISmsSender" 的约定注册。
        services.RemoveAll<Volo.Abp.Sms.ISmsSender>();
        services.AddTransient<Volo.Abp.Sms.ISmsSender, Sms.SmsSenderResolver>();
    }
}
