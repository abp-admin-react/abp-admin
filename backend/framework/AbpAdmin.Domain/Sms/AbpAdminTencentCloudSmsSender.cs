using System;
using System.Linq;
using System.Threading.Tasks;
using AbpAdmin.Http;
using AbpAdmin.Settings;
using EasyAbp.Abp.TencentCloud.Common;
using EasyAbp.Abp.TencentCloud.Common.Models;
using EasyAbp.Abp.TencentCloud.Common.Requester;
using EasyAbp.Abp.TencentCloud.Sms.SendSms;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Settings;
using Volo.Abp.Sms;

namespace AbpAdmin.Sms;

/// <summary>
/// 腾讯云短信发送（T3.5）。不用 EasyAbp.Abp.Sms.TencentCloud 包装包的 TencentCloudSmsSender：
/// 它的凭据设置项（EasyAbp.Abp.Sms.TencentCloud.DefaultSecretKey 等）定义时 isEncrypted: false（1.21.0
/// 反编译核实），不满足"厂商凭据必须加密"的要求。本类请求构造复用底层包
/// EasyAbp.Abp.TencentCloud.Sms/Common 的公共类型，凭据改读我们自己的加密设置项。
///
/// SmsMessage.Properties 约定 key（与 EasyAbp 包装包一致）：
///   "TemplateID"       模板 ID，必填
///   "TemplateParamSet" 模板参数（string[] 或 JSON 数组字符串），按位置填充
/// 其余（Sign/SmsSdkAppid/EndPoint/Region/SecretId/SecretKey）走 AbpAdmin.Sms.TencentCloud.* 设置项。
/// </summary>
[ExposeServices(typeof(AbpAdminTencentCloudSmsSender))]
public class AbpAdminTencentCloudSmsSender : ISmsSender, ITransientDependency
{
    public const string TemplateIdPropertyKey = "TemplateID";
    public const string TemplateParamSetPropertyKey = "TemplateParamSet";

    private readonly ISettingProvider _settingProvider;
    private readonly ITencentCloudApiRequester _requester;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AbpAdminTencentCloudSmsSender> _logger;

    public AbpAdminTencentCloudSmsSender(
        ISettingProvider settingProvider,
        ITencentCloudApiRequester requester,
        IConfiguration configuration,
        ILogger<AbpAdminTencentCloudSmsSender> logger)
    {
        _settingProvider = settingProvider;
        _requester = requester;
        _configuration = configuration;
        _logger = logger;
    }

    public virtual async Task SendAsync(SmsMessage smsMessage)
    {
        var templateId = SmsMessagePropertyReader.GetString(smsMessage, TemplateIdPropertyKey);
        var appid = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.TencentCloudSmsSdkAppid);
        var secretId = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.TencentCloudSecretId);
        var secretKey = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.TencentCloudSecretKey);

        if (string.IsNullOrWhiteSpace(secretId) || string.IsNullOrWhiteSpace(secretKey) || string.IsNullOrWhiteSpace(appid))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.SmsTencentCloudCredentialNotConfigured);
        }

        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.SmsTemplateNotConfigured)
                .WithData("Provider", AbpAdminSmsProviders.TencentCloud);
        }

        var endPoint = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.TencentCloudEndPoint);
        var sign = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.TencentCloudSign);
        var region = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.TencentCloudRegion);

        // M-5（SSRF 防线）：EndPoint 是管理员可改设置项（默认 sms.tencentcloudapi.com），
        // 被改成内网/保留地址（如 169.254.169.254）时本服务即成出站跳板。与 Webhook/探活
        // 同一防线（SafeHttpUrl：IP 字面量直查网段表、域名 DNS 解析后逐个检查）；放行开关
        // Sms:AllowIntranetTargets 显式 true 才豁免（内网中转网关场景）。EasyAbp requester
        // 用裸 HttpClient（无禁重定向），端点侧校验是本仓库能加的闸口；发送前逐次校验，
        // DNS rebinding 残余与 Webhook 同口径（见 SafeHttpUrl 类注释）。
        // 2026-10-10 极端轮实测：十进制(2130706433)/十六进制(0x7f.0.0.1)/userinfo 伪装
        // （域名@169.254.169.254）/v4-mapped([::ffff:127.0.0.1]) 四类绕过变体全部被拦
        //（IPAddress.TryParse/IsIPv4MappedToIPv6 归一后命中网段表）。
        if (!string.IsNullOrWhiteSpace(endPoint))
        {
            var endPointUrl = endPoint.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                              endPoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? endPoint
                : $"https://{endPoint.TrimEnd('/')}/";
            var blockedHost = await SafeHttpUrl.GetBlockedHostAsync(endPointUrl, _configuration, "Sms:AllowIntranetTargets");
            if (blockedHost != null)
            {
                throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.SmsEndpointBlocked)
                    .WithData("Host", blockedHost);
            }
        }

        // 与 EasyAbp 包装包同形的手机号归一化：11 位 1 开头的大陆手机号补 +86
        var phoneNumber = smsMessage.PhoneNumber.StartsWith("1") && smsMessage.PhoneNumber.Length == 11
            ? "+86" + smsMessage.PhoneNumber
            : smsMessage.PhoneNumber;

        var request = new SendSmsRequest(
            new[] { phoneNumber },
            templateId,
            appid,
            sign,
            SmsMessagePropertyReader.GetStringArray(smsMessage, TemplateParamSetPropertyKey));

        var response = await _requester.SendRequestAsync<SendSmsResponse>(request, endPoint, new AbpTencentCloudCommonOptions
        {
            SecretId = secretId,
            SecretKey = secretKey,
            Region = region
        });

        string? errorCode = null;
        string? errorMessage = null;
        if (response.Error != null)
        {
            errorCode = response.Error.Code;
            errorMessage = response.Error.Message;
        }
        else if (response.SendStatusSet is { Count: > 0 } && response.SendStatusSet.First().Code != "Ok")
        {
            errorCode = response.SendStatusSet.First().Code;
            errorMessage = response.SendStatusSet.First().Message;
        }

        if (errorCode != null)
        {
            _logger.LogWarning("腾讯云短信发送失败：[{Code}] {Message}，手机号 {PhoneNumber}",
                errorCode, errorMessage, AbpAdminSmsLogMasker.MaskPhoneNumber(smsMessage.PhoneNumber));
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.SmsSendFailed)
                .WithData("Provider", AbpAdminSmsProviders.TencentCloud)
                .WithData("ProviderError", $"[{errorCode}] {errorMessage}");
        }
    }
}
