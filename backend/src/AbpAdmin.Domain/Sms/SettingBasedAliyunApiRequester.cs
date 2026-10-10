using System;
using System.Net.Http;
using System.Threading.Tasks;
using AbpAdmin.Http;
using AbpAdmin.Settings;
using EasyAbp.Abp.Aliyun.Common;
using EasyAbp.Abp.Aliyun.Common.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Json;
using Volo.Abp.Settings;

namespace AbpAdmin.Sms;

/// <summary>
/// 基于设置项的阿里云 API 请求器（T3.5），替换 EasyAbp 的 DefaultAliyunApiRequester。
/// 替换理由：默认实现从 IOptions&lt;AbpAliyunOptions&gt;（配置文件）读 AccessKeyId/Secret，
/// 不支持 SettingUi 与多租户；本实现改从加密设置项读取（1.21.0 反编译核实默认实现的行为）。
/// 请求签名仍复用 EasyAbp 的 ICommonRequest.SetCommonParameters/SetSignature 公共方法。
/// </summary>
[Dependency(ReplaceServices = true)]
[ExposeServices(typeof(IAliyunApiRequester))]
public class SettingBasedAliyunApiRequester : IAliyunApiRequester, ITransientDependency
{
    private readonly ISettingProvider _settingProvider;
    private readonly IJsonSerializer _jsonSerializer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SettingBasedAliyunApiRequester> _logger;

    public SettingBasedAliyunApiRequester(
        ISettingProvider settingProvider,
        IJsonSerializer jsonSerializer,
        IHttpClientFactory httpClientFactory,
        IGuidGenerator guidGenerator,
        IConfiguration configuration,
        ILogger<SettingBasedAliyunApiRequester> logger)
    {
        _settingProvider = settingProvider;
        _jsonSerializer = jsonSerializer;
        _httpClientFactory = httpClientFactory;
        _guidGenerator = guidGenerator;
        _configuration = configuration;
        _logger = logger;
    }

    public virtual async Task<TResponse> SendRequestAsync<TResponse>(ICommonRequest request, string url)
        where TResponse : ICommonResponse
    {
        var accessKeyId = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.AliyunAccessKeyId);
        var accessKeySecret = await _settingProvider.GetOrNullAsync(AbpAdminSettings.Sms.AliyunAccessKeySecret);

        if (string.IsNullOrWhiteSpace(accessKeyId) || string.IsNullOrWhiteSpace(accessKeySecret))
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.SmsAliyunCredentialNotConfigured);
        }

        request.SetCommonParameters(accessKeyId, _guidGenerator.Create());
        request.SetSignature(accessKeySecret);

        if (!request.IsReady())
        {
            throw new AbpException("阿里云 API 公共参数没有正确配置。");
        }

        // M-5（SSRF 防线）：url 当前由 EasyAbp 包构造（非管理员可控），但出站 URL 按
        // "一律过闸"的口径统一——包未来若引入可配置端点，这里已经是闸口。与 Webhook/
        // 探活/腾讯云 SMS 同一 SafeHttpUrl；放行开关 Sms:AllowIntranetTargets 显式
        // true 才豁免。
        var blockedHost = await SafeHttpUrl.GetBlockedHostAsync(url, _configuration, "Sms:AllowIntranetTargets");
        if (blockedHost != null)
        {
            throw new BusinessException(AbpAdminDomainErrorCodes.Notifications.SmsEndpointBlocked)
                .WithData("Host", blockedHost);
        }

        var client = _httpClientFactory.CreateClient();
        // 序列化参数与 EasyAbp DefaultAliyunApiRequester 逐字对齐（camelCase: true），避免行为漂移
        using var httpRequest = request.Method == HttpMethod.Get
            ? new HttpRequestMessage(HttpMethod.Get, url + "?" + request.GetQueryString())
            : new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(_jsonSerializer.Serialize(request.RequestParameters, true, false))
            };

        _logger.LogDebug("阿里云 API 请求：{Method} {Url}", httpRequest.Method, url);
        var response = await client.SendAsync(httpRequest);
        var content = await response.Content.ReadAsStringAsync();

        return _jsonSerializer.Deserialize<TResponse>(content, true);
    }
}
