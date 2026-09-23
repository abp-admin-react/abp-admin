using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Sms;

namespace AbpAdmin.Sms;

/// <summary>
/// 短信厂商未配置时的兜底 sender（T3.5）：不抛异常，打一条 Warning 让"短信没发"在日志里可见。
/// 与 NullEmailSender 同哲学；不用框架自带的 NullSmsSender 是因为它的日志没有指出
/// "厂商未配置"这个真正原因（且我们希望日志里手机号打码）。
/// </summary>
[ExposeServices(typeof(NullAbpAdminSmsSender))]
public class NullAbpAdminSmsSender : ISmsSender, ITransientDependency
{
    private readonly ILogger<NullAbpAdminSmsSender> _logger;

    public NullAbpAdminSmsSender(ILogger<NullAbpAdminSmsSender> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(SmsMessage smsMessage)
    {
        _logger.LogWarning(
            "短信厂商未配置（设置项 AbpAdmin.Sms.Provider 为 Null 或未设置），消息未发送。手机号：{PhoneNumber}",
            AbpAdminSmsLogMasker.MaskPhoneNumber(smsMessage.PhoneNumber));
        return Task.CompletedTask;
    }
}
