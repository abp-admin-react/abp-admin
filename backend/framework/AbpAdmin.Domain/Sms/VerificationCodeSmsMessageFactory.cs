using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.Settings;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Json;
using Volo.Abp.Settings;
using Volo.Abp.Sms;

namespace AbpAdmin.Sms;

/// <summary>
/// 手机验证码短信的消息构造器（T3.5，供 T2.7 的验证码路径使用）。
/// 把"发一个验证码"翻译成两家厂商各自的消息形状：
/// - 阿里云：Text = 模板参数 JSON（{"code":"123456"}），模板 CODE 走 Properties["TemplateCode"]；
/// - 腾讯云：Text 不被读取，参数走 Properties["TemplateParamSet"]（按位置），模板 ID 走 Properties["TemplateID"]。
/// 模板编码两家复用同一个设置项 AbpAdmin.Sms.VerificationCode.TemplateCode；
/// 模板内参数名约定为 code（腾讯模板按位置取第一个参数）。
/// 两套属性同时放进消息，各 sender 只读自己认识的 key。
/// </summary>
public class VerificationCodeSmsMessageFactory : ITransientDependency
{
    private readonly ISettingProvider _settingProvider;
    private readonly IJsonSerializer _jsonSerializer;

    public VerificationCodeSmsMessageFactory(ISettingProvider settingProvider, IJsonSerializer jsonSerializer)
    {
        _settingProvider = settingProvider;
        _jsonSerializer = jsonSerializer;
    }

    public virtual async Task<SmsMessage> BuildAsync(string phoneNumber, string code)
    {
        var templateCode = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.VerificationCodeTemplateCode);

        var templateParamJson = _jsonSerializer.Serialize(new Dictionary<string, string> { ["code"] = code });
        var message = new SmsMessage(phoneNumber, templateParamJson);

        // 模板编码未配置时不放 key，让具体 sender 抛"模板未配置"的明确异常
        if (templateCode != null)
        {
            message.Properties[AliyunSmsSender.TemplateCodePropertyKey] = templateCode;
            message.Properties[AbpAdminTencentCloudSmsSender.TemplateIdPropertyKey] = templateCode;
        }
        message.Properties[AbpAdminTencentCloudSmsSender.TemplateParamSetPropertyKey] = new[] { code };

        // 打用途标记：SmsSenderResolver 据此走验证码限流策略（未打标的消息走 SmsNotification 策略）
        message.Properties[SmsMessagePropertyKeys.Purpose] = SmsMessagePropertyKeys.PurposeVerificationCode;

        return message;
    }
}
