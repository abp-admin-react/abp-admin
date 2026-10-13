using System;
using System.Threading.Tasks;
using AbpAdmin.RateLimiting;
using AbpAdmin.Settings;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;
using Volo.Abp.Sms;

namespace AbpAdmin.Sms;

/// <summary>
/// ISmsSender 的唯一入口（T3.5）：按设置项 AbpAdmin.Sms.Provider 分发到具体厂商。
/// 注册在 AbpAdminDomainModule（先 RemoveAll&lt;ISmsSender&gt; 再显式登记本类）。
///
/// 要点：
/// - 按具体类型解析厂商，绝不解析 ISmsSender（否则会解析回自己，无限递归）。
/// - 厂商按设置项而不是配置文件切换，是因为多租户：不同租户可能用不同厂商/签名/模板，
///   ISettingProvider 天然支持租户级覆盖，配置文件不支持。
/// - 限流落在本类（而不是各调用方），所有短信路径都被覆盖。超限抛
///   AbpAdminOperationRateLimitingException（HTTP 429），本类不 catch——用户主动触发的路径
///   （验证码等）需要 429 告诉用户"稍后再试"；fan-out 路径由 NotificationService 的
///   SmsNotificationManager 逐条 catch 记为失败，不会拖垮整批。
/// - 分区键用完整手机号（打码后不同号码会撞成同一个分区）；打码只发生在日志/异常渲染层。
/// </summary>
[ExposeServices(typeof(SmsSenderResolver))]
public class SmsSenderResolver : ISmsSender, ITransientDependency
{
    private readonly ISettingProvider _settingProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOperationRateLimitingChecker _rateLimitingChecker;

    public SmsSenderResolver(
        ISettingProvider settingProvider,
        IServiceProvider serviceProvider,
        IOperationRateLimitingChecker rateLimitingChecker)
    {
        _settingProvider = settingProvider;
        _serviceProvider = serviceProvider;
        _rateLimitingChecker = rateLimitingChecker;
    }

    public virtual async Task SendAsync(SmsMessage smsMessage)
    {
        // T2.5 限流：同一手机号按用途分策略。验证码消息由 VerificationCodeSmsMessageFactory
        // 打了 Purpose 标记，走验证码策略（1 小时 3 次）；其余（NotificationService fan-out 通知等）
        // 走通知策略（阈值独立配置），避免运营批量通知撞验证码阈值被静默记失败。
        // CheckAsync = 检查并递增；超限时它自己抛 429 异常，不需要再判一次。
        var policy = SmsMessagePropertyReader.GetString(smsMessage, SmsMessagePropertyKeys.Purpose)
            == SmsMessagePropertyKeys.PurposeVerificationCode
            ? SmsRateLimitingPolicies.VerificationCode
            : SmsRateLimitingPolicies.Notification;
        await _rateLimitingChecker.CheckAsync(policy, smsMessage.PhoneNumber);

        var provider = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.Provider);

        var sender = provider switch
        {
            AbpAdminSmsProviders.Aliyun => (ISmsSender)_serviceProvider.GetRequiredService<AliyunSmsSender>(),
            AbpAdminSmsProviders.TencentCloud => _serviceProvider.GetRequiredService<AbpAdminTencentCloudSmsSender>(),
            _ => _serviceProvider.GetRequiredService<NullAbpAdminSmsSender>(),
        };

        await sender.SendAsync(smsMessage);
    }
}
