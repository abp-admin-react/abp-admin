using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AbpAdmin.Settings;
using EasyAbp.Abp.Aliyun.Sms.Services;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Json;
using Volo.Abp.Settings;
using Volo.Abp.Sms;

namespace AbpAdmin.Sms;

/// <summary>
/// 把 EasyAbp.Abp.Aliyun.Sms 的 AliyunSmsService 适配到 ABP 的 ISmsSender（T3.5）。
/// 该包本身不实现 ISmsSender（1.21.0 反编译核实）。
///
/// SmsMessage.Properties 约定 key：
///   "SignName"     阿里云短信签名；缺省回落到设置项 AbpAdmin.Sms.Aliyun.SignName
///   "TemplateCode" 阿里云模板 CODE，必填
/// SmsMessage.Text 被当作模板参数的 JSON（阿里云的 TemplateParam），不是短信正文。
///
/// 凭据不走 EasyAbp 的 AbpAliyunOptions（IOptions 配置文件方案，不支持 SettingUi/多租户），
/// 而由 SettingBasedAliyunApiRequester 从加密设置项读取。
///
/// 只暴露自身类型：ISmsSender 在 DI 里的唯一实现是 SmsSenderResolver
/// （类名以 SmsSender 结尾会被 ABP 约定注册当成默认接口 ISmsSender 暴露，必须显式收窄）。
/// </summary>
[ExposeServices(typeof(AliyunSmsSender))]
public class AliyunSmsSender : ISmsSender, ITransientDependency
{
    public const string SignNamePropertyKey = "SignName";
    public const string TemplateCodePropertyKey = "TemplateCode";

    private readonly AliyunSmsService _smsService;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly ISettingProvider _settingProvider;
    private readonly ILogger<AliyunSmsSender> _logger;

    public AliyunSmsSender(
        AliyunSmsService smsService,
        IJsonSerializer jsonSerializer,
        ISettingProvider settingProvider,
        ILogger<AliyunSmsSender> logger)
    {
        _smsService = smsService;
        _jsonSerializer = jsonSerializer;
        _settingProvider = settingProvider;
        _logger = logger;
    }

    public virtual async Task SendAsync(SmsMessage smsMessage)
    {
        var signName = SmsMessagePropertyReader.GetString(smsMessage, SignNamePropertyKey)
            ?? await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.AliyunSignName);
        var templateCode = SmsMessagePropertyReader.GetString(smsMessage, TemplateCodePropertyKey);

        if (string.IsNullOrWhiteSpace(signName) || string.IsNullOrWhiteSpace(templateCode))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.SmsTemplateNotConfigured)
                .WithData("Provider", AbpAdminSmsProviders.Aliyun);
        }

        // 凭据检查放在 sender（而不是只在 requester）：明确异常先于任何外呼，
        // 也让凭据缺失的报错不依赖请求器实现
        var accessKeyId = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.AliyunAccessKeyId);
        var accessKeySecret = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.AliyunAccessKeySecret);
        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(accessKeySecret))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.SmsAliyunCredentialNotConfigured);
        }

        // Text 是模板参数 JSON（如 {"code":"123456"}），反序列化成对象交给阿里云请求构造器再序列化，
        // 避免把 JSON 字符串二次编码。
        object templateContent;
        try
        {
            templateContent = _jsonSerializer.Deserialize<Dictionary<string, string>>(smsMessage.Text);
        }
        catch (Exception ex)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.SmsTemplateNotConfigured)
                .WithData("Provider", AbpAdminSmsProviders.Aliyun)
                .WithData("Reason", "Text 必须是模板参数 JSON")
                .WithData("Exception", ex.Message);
        }

        var response = await _smsService.SendMessageAsync(
            smsMessage.PhoneNumber,
            new AbpAdminSmsTemplate(signName, templateCode, templateContent));

        // 阿里云成功码固定为 "OK"（SmsCommonResponse : CommonResponse，含 Code/Message/BizId）
        if (response.Code != "OK")
        {
            _logger.LogWarning("阿里云短信发送失败：[{Code}] {Message}，手机号 {PhoneNumber}",
                response.Code, response.Message, AbpAdminSmsLogMasker.MaskPhoneNumber(smsMessage.PhoneNumber));
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.SmsSendFailed)
                .WithData("Provider", AbpAdminSmsProviders.Aliyun)
                .WithData("ProviderError", $"[{response.Code}] {response.Message}");
        }
    }
}
